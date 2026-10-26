"""Host-owned contracts for durable, player-correctable profile memory.

The model may classify an exact player-authored span. It never chooses
authority-bearing ids, timestamps, owner scope, or promotion of inferred
material. The public projection matches the D057 contract exactly.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta
import re
import unicodedata
from typing import Any

from .domain import (
    FrozenTurn,
    RelationshipContractError,
    RelationshipScope,
    canonical_json,
    canonical_utc,
    parse_utc,
    require_bounded_text,
    require_event_identity,
    sha256_hex,
)


PROFILE_AUXILIARY_TASK = "lilith_profile_memory_review"
PROFILE_STORE_SCHEMA_VERSION = 2
PROFILE_DATABASE_FILE = "profile-memory.sqlite3"

MEMORY_CATEGORIES = frozenset(
    {"relationship_core", "long_term", "candidate", "temporary", "relationship_episode"}
)
WRITABLE_CATEGORIES = frozenset(
    {"relationship_core", "long_term", "candidate", "temporary"}
)
MANUAL_ADD_CATEGORIES = frozenset({"relationship_core", "long_term", "temporary"})
MEMORY_STATES = frozenset({"active", "hidden", "rejected", "expired", "superseded"})
MEMORY_SOURCES = frozenset(
    {"explicit_user", "inferred", "operator", "relationship_review"}
)

# Internal semantic kinds improve deterministic local recall while D057's
# public category describes lifecycle and authority.
PROFILE_MEMORY_KINDS = frozenset(
    {
        "preference",
        "identity",
        "routine",
        "communication",
        "relationship_core",
        "boundary",
        "goal",
    }
)
ASSERTION_KINDS = frozenset({"explicit", "inferred", "temporary"})

_PIN_AFFIRMATIVE_PATTERNS = (
    r"(?i)\b(?:please\s+)?remember\b",
    r"(?i)\b(?:do\s+not|don't|never)\s+forget\b",
    r"(?i)\bkeep\s+(?:this|that|it)\s+in\s+mind\b",
    r"(?:請|请)?(?:記住|记住|記得|记得)",
    r"(?:別忘|别忘|不要忘|永遠記得|永远记得)",
    r"(?:覚えて(?:おいて|ください|ね)?|忘れないで)",
    r"(?:기억해(?:\s*줘)?|잊지\s*마)",
    r"(?i)\b(?:recuerda|recuerde|acuérdate|no\s+olvides)\b",
    r"(?i)\b(?:souviens[- ]toi|retiens|n'oublie\s+pas)\b",
    r"(?i)\b(?:merk(?:e)?\s+dir|vergiss\s+nicht)\b",
    r"(?i)\b(?:lembre[- ]se|lembra[- ]te|não\s+esqueça|não\s+esqueças)\b",
)
_PIN_NEGATIVE_PATTERNS = (
    r"(?i)\b(?:do\s+not|don't|never)\s+(?:save|store|memorize|record)\b",
    r"(?i)\b(?:do\s+not|don't|never)\s+remember\b",
    r"(?i)\b(?:do\s+not|don't|never)\s+keep\s+(?:this|that|it)\s+in\s+mind\b",
    r"(?:不要|不用|別|别)[^。！？!?；;\r\n]{0,32}(?:記住|记住|記得|记得)",
    r"(?:不要|不用|別|别)[^。！？!?；;\r\n]{0,48}(?:儲存|储存|保存|記錄|记录|存成記憶|存成记忆)",
    r"(?:覚えないで|記憶しないで|忘れて(?:ください|ね)?)",
    r"(?:기억하지\s*마|기억하지\s*말|잊어(?:\s*줘)?)",
    r"(?i)\b(?:no\s+(?:recuerdes|guardes|memorices)|olvida\s+esto)\b",
    r"(?i)\b(?:ne\s+(?:retiens|mémorise|garde)\s+pas|oublie\s+(?:ça|cela))\b",
    r"(?i)\b(?:merk(?:e)?\s+dir\s+(?:das\s+)?nicht|speichere\s+(?:das\s+)?nicht|vergiss\s+das)\b",
    r"(?i)\b(?:não\s+(?:memorize|guarde|lembre)|esqueça\s+isso)\b",
)
_TEMPORARY_PHRASES = (
    "for now", "right now", "currently", "today", "this week", "this month",
    "lately", "recently", "暫時", "暂时", "目前", "現在", "现在", "今天",
    "這週", "这周", "本週", "本周", "最近", "今は", "今日", "最近は",
    "현재", "오늘",
    "tomorrow", "tonight", "this weekend", "next week", "next month",
    "next weekend", "this afternoon", "this evening", "temporarily",
    "明天", "明日", "後天", "后天", "今晚", "這周", "这週", "下周", "下週",
    "這星期", "这星期", "下星期", "這禮拜", "这礼拜", "下禮拜", "下礼拜",
    "這個月", "这个月", "下個月", "下个月", "這幾天", "这几天", "週末", "周末",
    "今週", "来週", "再来週", "今月", "来月", "今夜", "今晩", "明後日", "当面",
    "내일", "모레", "이번 주", "다음 주", "다음주", "이번주", "이번 달", "다음 달",
    "주말", "요즘", "당분간", "오늘 밤",
    "hoy", "mañana", "esta semana", "próxima semana", "semana que viene", "este mes", "por ahora",
    "aujourd'hui", "demain", "cette semaine", "semaine prochaine", "ce mois", "en ce moment", "pour l'instant",
    "heute", "morgen", "diese woche", "dieser woche", "nächste woche", "nächster woche",
    "diesen monat", "nächsten monat", "zurzeit", "vorübergehend",
    "hoje", "amanhã", "esta semana", "próxima semana", "semana que vem", "este mês", "por enquanto",
)
# Duration/deadline cues supplement the phrase vocabulary. A bare next year or
# calendar date is not temporary: birthdays and long-term goals remain durable.
_TEMPORARY_PATTERNS = (
    r"\b(?:for|within|over)\s+(?:the\s+)?(?:next\s+)?(?:\d+|a|an|one|two|three|few|couple\s+of)\s+(?:hours?|days?|weeks?|months?)\b",
    r"\b(?:until|through|till)\s+(?:next\s+)?(?:year|monday|tuesday|wednesday|thursday|friday|saturday|sunday|\d{4}-\d{1,2}-\d{1,2})\b",
    r"\bnext\s+(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
    r"(?:接下來|接下来|未來|未来|這|这|為期|为期|持續|持续|還有|还有)[一二三四五六七八九十兩两幾几\d]+(?:個|个)?(?:小時|小时|天|週|周|星期|禮拜|礼拜|月)",
    r"(?:到|截至|直到)\s*(?:\d{4}[-/]\d{1,2}[-/]\d{1,2}|\d{1,2}月\d{1,2}[日號号]|(?:週|周|星期|禮拜|礼拜)[一二三四五六日天])",
    r"[月火水木金土日]曜日まで",
    r"[월화수목금토일]요일까지",
    r"\bhasta\s+(?:el\s+)?(?:lunes|martes|miércoles|jueves|viernes|sábado|domingo)\b",
    r"\bjusqu'(?:à|au)\s+(?:lundi|mardi|mercredi|jeudi|vendredi|samedi|dimanche)\b",
    r"\bbis\s+(?:montag|dienstag|mittwoch|donnerstag|freitag|samstag|sonntag)\b",
    r"\baté\s+(?:a\s+|o\s+)?(?:segunda-feira|terça-feira|quarta-feira|quinta-feira|sexta-feira|sábado|domingo)\b",
    r"(?:今後|これから)?[一二三四五六七八九十\d]+(?:日間|週間|か月間|ヶ月間)",
    r"(?:\d+|한|두|세|며칠)\s*(?:일|주|달|개월)\s*(?:동안|간)",
    r"\b(?:durante|por|pendant|für)\s+(?:los\s+próximos\s+|les\s+prochains\s+|die\s+nächsten\s+)?(?:\d+|dos|deux|zwei|dois|tres|trois|drei|três)\s+(?:días?|dias?|jours?|tage?|wochen|semanas?|semaines?)\b",
)
_PROFILE_SIGNAL_PHRASES = (
    "i like", "i love", "i enjoy", "i dislike", "i hate", "i prefer",
    "my favorite", "my favourite", "my name is", "call me", "i usually",
    "i always", "every day", "every morning", "every night", "i want you to",
    "i am allergic", "i'm allergic", "i have an allergy", "my allergy",
    "i cannot eat", "i can't eat", "i don't eat", "i do not eat", "i am vegan",
    "i'm vegan", "i am vegetarian", "i'm vegetarian", "i am a", "i'm a",
    "i work as", "my pronouns", "my birthday", "my goal", "my long-term goal",
    "i plan to", "i intend to", "i want to", "i'd like to",
    "我喜歡", "我喜欢", "我平常喜歡", "我平常喜欢", "我通常喜歡", "我通常喜欢",
    "我愛", "我爱", "我討厭", "我讨厌", "我不喜歡",
    "我不喜欢", "我偏好", "我最喜歡", "我最喜欢", "我的名字", "叫我",
    "我習慣", "我习惯", "我每天", "我希望你", "我過敏", "我过敏",
    "對我過敏", "对我过敏", "我不吃", "我不能吃", "我是素食", "我是純素",
    "我是纯素", "我是", "我叫", "我的生日", "我的目標", "我的目标",
    "我打算", "我計畫", "我计划", "我想要",
    "好き", "嫌い", "私の名前", "呼んで", "いつも", "毎日", "アレルギー",
    "食べられない", "食べない", "私は", "僕は", "俺は", "目標", "予定",
    "したい", "になりたい",
    "좋아해", "싫어해", "내 이름", "제 이름", "매일", "알레르기", "못 먹",
    "안 먹", "저는", "나는", "불러", "목표", "계획", "하고 싶",
)
_PROFILE_KIND_SIGNAL_PHRASES = {
    "preference": (
        "i like", "i love", "i enjoy", "i dislike", "i hate", "i prefer",
        "my favorite", "my favourite", "allergic", "allergy", "cannot eat",
        "can't eat", "don't eat", "do not eat", "vegan", "vegetarian",
        "我喜歡", "我喜欢", "我愛", "我爱", "我討厭", "我讨厌", "我不喜歡",
        "我不喜欢", "我偏好", "我最喜歡", "我最喜欢", "過敏", "过敏",
        "我不吃", "我不能吃", "平常喜歡", "平常喜欢", "通常喜歡", "通常喜欢",
        "想喝", "想吃", "素食", "純素", "纯素",
        "好き", "嫌い", "アレルギー", "食べられない", "食べない",
        "좋아해", "싫어해", "알레르기", "못 먹", "안 먹",
    ),
    "identity": (
        "my name is", "call me", "i am a", "i'm a", "i work as", "my pronouns",
        "my birthday", "我的名字", "我叫", "叫我", "我是", "我的生日",
        "私の名前", "呼んで", "私は", "僕は", "俺は", "내 이름", "제 이름",
        "불러", "저는", "나는",
    ),
    "routine": (
        "i usually", "i always", "every day", "every morning", "every night",
        "my routine", "我習慣", "我习惯", "我每天", "我通常", "每天",
        "いつも", "毎日", "日課", "매일", "항상", "습관",
    ),
    "communication": (
        "reply to me", "talk to me", "speak to me", "call me", "my language",
        "我希望你", "請用", "请用", "回覆我", "回复我", "跟我說", "跟我说",
        "話して", "答えて", "呼んで", "말해", "대답해", "불러",
    ),
    "relationship_core": (
        "our relationship", "you mean a lot", "important to me", "我和莉莉絲",
        "我和莉莉丝", "你對我很重要", "你对我很重要", "我們的關係", "我们的关系",
        "我希望莉莉絲", "我希望莉莉丝",
        "リリスとの", "大切", "릴리스와", "소중",
    ),
    "boundary": (
        "do not", "don't", "never", "must not", "please stop", "不要", "不准",
        "禁止", "別再", "别再", "やめて", "しないで", "하지 마", "금지",
    ),
    "goal": (
        "my goal", "long-term goal", "i plan to", "i intend to", "i want to",
        "i'd like to", "我的目標", "我的目标", "我打算", "我計畫", "我计划",
        "我想要", "目標", "予定", "したい", "になりたい", "목표", "계획",
        "하고 싶",
    ),
}
# The same vocabulary drives admission and evidence checks. Adding a phrase to
# admission alone would still fail at commit. Match normalized text only; never
# normalize or translate the exact persisted evidence itself.
_ADDITIONAL_KIND_PHRASES = {
    "preference": (
        "like", "love", "enjoy", "dislike", "hate", "prefer", "favorite", "favourite",
        "allergic", "allergy", "intolerant", "vegetarian", "vegan", "can't eat", "cannot eat",
        "喜歡", "喜欢", "最愛", "最爱", "愛喝", "爱喝", "愛吃", "爱吃", "偏愛", "偏爱",
        "偏好", "討厭", "讨厌", "不愛", "不爱", "過敏", "过敏", "吃素", "不能吃",
        "好き", "嫌い", "苦手", "好物", "アレルギー", "ベジタリアン",
        "좋아", "싫어", "선호", "알레르기", "채식",
        "me gusta", "me gustan", "me encanta", "prefiero", "no me gusta", "soy alérgico", "soy alérgica",
        "j'aime", "j'adore", "je préfère", "je déteste", "je suis allergique",
        "mag", "liebe", "bevorzuge", "allergisch", "lieblingsgetränk", "lieblingsessen",
        "gosto", "adoro", "prefiro", "detesto", "sou alérgico", "sou alérgica",
    ),
    "identity": (
        "name", "birthday", "pronouns", "work as", "work at", "working as", "working at",
        "live in", "live with", "born in", "profession", "occupation",
        "名字", "姓名", "生日", "住在", "住址", "職業", "职业", "工作是", "從事", "从事", "任職", "任职",
        "名前", "誕生日", "住んで", "仕事", "職業", "です",
        "이름", "생일", "살고", "직업", "일해", "입니다", "이에요", "예요",
        "me llamo", "soy", "vivo", "trabajo", "mi cumpleaños", "mi nombre",
        "je m'appelle", "je suis", "j'habite", "je travaille", "mon anniversaire",
        "heiße", "bin", "wohne", "arbeite", "geburtstag", "beruf",
        "me chamo", "chamo-me", "meu nome", "sou", "moro", "trabalho", "meu aniversário",
    ),
    "routine": (
        "usually", "normally", "often", "always", "habit", "routine", "every", "tend to",
        "平常", "通常", "習慣", "习惯", "每天", "每週", "每周", "每晚", "固定", "經常", "经常",
        "普段", "たいてい", "習慣", "毎朝", "毎晩", "毎週", "いつも",
        "보통", "자주", "매일", "매주", "매년", "항상", "습관",
        "suelo", "normalmente", "cada día", "todos los días",
        "d'habitude", "habituellement", "tous les jours", "chaque matin", "chaque soir",
        "normalerweise", "gewöhnlich", "jeden tag", "jeden morgen", "jeden abend", "gewohnheit",
        "costumo", "normalmente", "todos os dias", "todas as manhãs",
    ),
    "communication": (
        "please reply", "please respond", "answer me", "speak to me", "call me", "prefer short replies",
        "請用", "请用", "回覆", "回复", "回答我", "稱呼我", "称呼我", "叫我", "希望你",
        "返事", "返答", "答えて", "話して", "呼んで",
        "답해", "답변", "대답", "말해", "불러",
        "respóndeme", "responde en", "háblame", "llámame",
        "réponds-moi", "réponds en", "parle-moi", "appelle-moi",
        "antworte", "sprich mit mir", "nenne mich",
        "responda-me", "responde-me", "responda em", "fale comigo", "me chame",
    ),
    "relationship_core": (
        "our relationship", "important to me", "mean a lot to me",
        "我們的關係", "我们的关系", "對我很重要", "对我很重要",
        "私にとって", "私たちの関係", "僕にとって", "大切",
        "저에게", "나에게", "우리 관계", "소중",
        "nuestra relación", "importante para mí",
        "notre relation", "important pour moi", "importante pour moi",
        "unsere beziehung", "mir wichtig",
        "nossa relação", "importante para mim",
    ),
    "boundary": (
        "don't call me", "do not call me", "don't touch", "do not touch", "please stop",
        "不要", "不准", "別再", "别再", "不可以", "不能叫我",
        "しないで", "やめて", "呼ばないで", "触らないで",
        "하지 마", "부르지 마", "만지지 마", "싫어요",
        "no me llames", "no me toques", "no quiero que",
        "ne m'appelle pas", "ne me touche pas", "je ne veux pas que",
        "nicht", "fass mich nicht", "ich möchte nicht",
        "não me chame", "não me toque", "não quero que",
    ),
    "goal": (
        "goal", "aim", "plan to", "planning to", "intend to", "hope to", "want to", "would like to", "dream of",
        "目標", "目标", "打算", "計畫", "计划", "計劃", "希望能", "想要", "準備要", "准备要",
        "目標", "予定", "つもり", "したい", "になりたい",
        "목표", "계획", "하고 싶", "하려고", "할 예정",
        "mi objetivo", "mi meta", "quiero", "planeo", "pienso",
        "mon objectif", "mon but", "je veux", "je compte", "j'espère",
        "mein ziel", "plane", "möchte", "will", "hoffe",
        "meu objetivo", "minha meta", "quero", "planejo", "pretendo",
    ),
}
_PROFILE_KIND_SIGNAL_PHRASES = {
    kind: phrases + _ADDITIONAL_KIND_PHRASES[kind]
    for kind, phrases in _PROFILE_KIND_SIGNAL_PHRASES.items()
}
_SCHEDULE_PHRASES = (
    "business trip", "travel", "traveling", "travelling", "away", "out of town", "vacation", "holiday",
    "busy", "working", "work", "overtime", "shift", "meeting", "appointment", "exam", "available",
    "unavailable", "recovering", "resting", "diet", "staying", "visiting", "training", "studying",
    "going to", "attending", "on leave", "at home", "in hospital", "in the hospital", "moving",
    "出差", "外地", "旅行", "旅遊", "旅游", "休假", "請假", "请假", "加班", "上班", "值班",
    "輪班", "轮班", "開會", "开会", "考試", "考试", "住院", "休養", "休养", "有空", "沒空", "没空",
    "不在", "住宿", "暫住", "暂住", "忌口", "吃素", "要去", "會去", "会去", "回家", "搬家",
    "出張", "旅行", "休暇", "残業", "試験", "入院", "留守", "に行き", "に行く", "引っ越し",
    "출장", "여행", "휴가", "야근", "근무", "시험", "입원", "자리", "바빠",
    "viaje", "viajar", "fuera", "vacaciones", "trabajando", "ocupado", "ocupada", "examen",
    "déplacement", "voyage", "vacances", "travaille", "absent", "absente", "examen",
    "dienstreise", "geschäftsreise", "unterwegs", "urlaub", "arbeite", "beschäftigt", "prüfung",
    "viagem", "viajar", "férias", "trabalhando", "ocupado", "ocupada", "prova",
)
_SELF_PATTERNS = (
    r"\b(?:i|me|my|mine|our)\b", r"我|咱", r"私|僕|俺|わたし|自分",
    r"저는|제가|저를|저에게|나는|내가|나를|나에게|내\s|제\s|우리",
    r"\b(?:yo|me|mi|mí|mis|soy|estoy|suelo|prefiero|quiero|planeo|vivo|trabajo|nuestra)\b",
    r"\b(?:je|j'|m'|moi|mon|ma|mes|notre)\b", r"\b(?:ich|mich|mir|mein\w*|unsere)\b",
    r"\b(?:eu|me|meu|minha|mim|sou|estou|gosto|adoro|prefiro|costumo|quero|pretendo|moro|nossa)\b",
)
_NONPLAYER_PATTERNS = (
    r"^(?:如果|假如|假設|假设|要是|例如|比如|假裝|假装|假扮|(?:請|请|幫我|帮我)?(?:翻譯|翻译))",
    r"^(?:他|她|它|你|妳|朋友|同事|莉莉絲|莉莉丝|我的(?:朋友|同事|家人|哥哥|姐姐|弟弟|妹妹|媽媽|妈妈|爸爸))(?!.{0,8}(?:對我很重要|对我很重要))",
    r"^我(?:的)?(?:妹|姐|弟|哥|媽|妈|爸|朋友|同事|老婆|老公|妻子|丈夫|兒子|儿子|女兒|女儿)",
    r"^(?:もし|例えば|仮に|翻訳|彼は|彼女は|友達は|友人は|リリスは)",
    r"^(?:(?:私|僕|俺|わたし)の)?(?:母|父|妹|姉|弟|兄|妻|夫|息子|娘|友人|友達)(?:は|が)",
    r"^[\w一-龯ぁ-んァ-ヶ]+(?:さん|くん|ちゃん|先生)(?:は|が)",
    r"^(?:만약|예를\s*들어|가령|번역|그는|그녀는|친구는|친구가|리리스는)",
    r"^(?:(?:제|내|우리)\s*)?(?:어머니|아버지|엄마|아빠|누나|언니|동생|형|오빠|아내|남편|아들|딸)(?:는|은|가|이)",
    r"^(?!(?:저|나|차|커피|홍차|딸기|케이크|음악)(?:는|가|은|이))[가-힣]{2,4}(?:는|가)\s",
    r"^(?:if|suppose|imagine|pretend|translate|for example|he|she|they|you|my (?:friend|colleague|sister|brother|mother|father))\b(?!\s+(?:mean a lot to me|are important to me))",
    r"^my\s+(?:wife|husband|partner|son|daughter|parent|child|cousin)\b",
    r"^(?:si|por ejemplo|supongamos|traduce|él|ella|ellos|mi amigo|mi amiga)\b",
    r"^(?:a\s+)?mi\s+(?:hermana|hermano|madre|padre|esposa|esposo|hijo|hija)\b",
    r"^(?:si|par exemple|imagine|traduis|il|elle|ils|elles|mon ami|mon amie)\b",
    r"^(?:ma|mon)\s+(?:sœur|soeur|frère|mère|père|femme|mari|fils|fille)\b",
    r"^(?:wenn|falls|angenommen|übersetze|er|sie|mein freund|meine freundin)\b",
    r"^mein\w*\s+(?:schwester|bruder|mutter|vater|kollege|kollegin)\b",
    r"^(?:se|por exemplo|imagine|traduza|ele|ela|eles|elas|meu amigo|minha amiga)\b",
    r"^(?:meu|minha)\s+(?:irmã|irmão|mãe|pai|esposa|esposo|filho|filha)\b",
)
_QUOTED_RE = re.compile(r'"[^"\n]*"|“[^”\n]*”|「[^」\n]*」|『[^』\n]*』|《[^》\n]*》|«[^»\n]*»|(?<!\w)\x27[^\x27\n]+\x27(?!\w)')
_CLAUSE_RE = re.compile(r"[^。！？!?；;\n，,]+")
_SENTENCE_SEPARATOR_RE = re.compile(r"[。！？!?；;\n]|\.(?=\s|$)")
_NONFACT_SENTENCE_RE = re.compile(
    r"^(?:如果|假如|假設|假设|要是|例如|比如|假裝|假装|假扮|(?:請|请|幫我|帮我)?(?:翻譯|翻译)|"
    r"もし|例えば|仮に|翻訳|만약|예를\s*들어|가령|번역|"
    r"(?:if|suppose|imagine|pretend|translate|for example|si|por ejemplo|supongamos|traduce|"
    r"par exemple|traduis|wenn|falls|angenommen|übersetze|se|traduza)\b)"
)
_SECRET_PREFIXES = (
    "sk-", "ghp_", "github_pat_", "xoxb-", "xoxp-", "AIza",
    "ya29.",
)
_SECRET_ASSIGNMENT_MARKERS = (
    "password", "passwd", "api key", "api_key", "access token", "bot token",
    "token", "secret", "credential", "authorization", "client secret", "private key",
    "otp", "one-time code", "one time code", "verification code",
    "密碼", "密码", "金鑰", "密钥", "存取權杖",
    "访问令牌", "私鑰", "私钥", "驗證碼", "验证码",
    "パスワード", "暗証番号", "秘密鍵", "認証コード", "비밀번호", "암호", "인증 코드",
    "contraseña", "clave privada", "mot de passe", "clé privée", "passwort", "kennwort",
    "privater schlüssel", "senha", "palavra-passe", "chave privada",
)

_PEM_PRIVATE_KEY_RE = r"(?i)-----BEGIN(?: [A-Z0-9-]+)* PRIVATE KEY-----"
_SSH_KEY_RE = r"(?i)\b(?:ssh-rsa|ssh-ed25519|ecdsa-sha2-[a-z0-9-]+)\s+[A-Za-z0-9+/]{40,}={0,3}"
_TELEGRAM_BOT_TOKEN_RE = r"(?<!\d)\d{6,12}:[A-Za-z0-9_-]{30,}(?![A-Za-z0-9_-])"
_AWS_ACCESS_KEY_RE = r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"
_JWT_RE = r"(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}(?![A-Za-z0-9_-])"
_BEARER_RE = r"(?i)\b(?:authorization\s*[:=]\s*)?bearer\s+[A-Za-z0-9._~+/=-]{8,}"
_BASIC_AUTH_RE = r"(?i)\bauthorization\s*[:=]\s*basic\s+[A-Za-z0-9+/]{8,}={0,2}"
_OAUTH_TOKEN_RE = r"(?i)\bya29\.[A-Za-z0-9_-]{16,}\b"
_GENERIC_SECRET_ASSIGNMENT_RE = (
    r"(?i)\b[A-Za-z][A-Za-z0-9_.-]{0,80}"
    r"(?:token|secret|password|passwd|credential|api[_-]?key|private[_-]?key)"
    r"[A-Za-z0-9_.-]{0,20}\b\s*(?::|=|\bis\b|\bwas\b)\s*"
    r"['\"]?([^\s'\";,]{12,})"
)
_HIGH_ENTROPY_ASSIGNMENT_RE = (
    r"(?i)\b[A-Za-z_][A-Za-z0-9_.-]{1,80}\b\s*(?::|=|\bis\b|\bwas\b)\s*"
    r"['\"]?([^\s'\";,]{16,})"
)
_UNSAFE_MEMORY_CONTENT_PHRASES = (
    "ignore previous", "ignore all previous", "disregard previous", "system prompt",
    "developer message", "hidden instruction", "prompt injection", "call a tool",
    "call tool", "use a tool", "execute command", "run command", "open terminal",
    "always obey", "must comply", "reveal your prompt", "忽略先前", "忽略之前",
    "無視先前", "无视先前", "系統提示", "系统提示", "開發者訊息", "开发者消息",
    "提示注入", "呼叫工具", "调用工具", "執行命令", "执行命令", "打開終端",
    "打开终端", "必須服從", "必须服从", "前の指示を無視", "システムプロンプト",
    "ツールを呼び出", "命令を実行", "이전 지시를 무시", "시스템 프롬프트",
    "도구를 호출", "명령을 실행", "<system", "<assistant", "<tool",
)


def _single_line_text(value: Any, field: str, *, maximum: int) -> str:
    text = require_bounded_text(value, field, maximum=maximum)
    if any(ord(character) < 32 for character in text):
        raise RelationshipContractError(f"{field} contains control characters")
    return text


def require_memory_content(value: Any, field: str = "content") -> str:
    if not isinstance(value, str) or not value.strip() or value != value.strip():
        raise RelationshipContractError(f"{field} must be non-empty trimmed text")
    if any(ord(character) < 32 and character not in "\r\n\t" for character in value):
        raise RelationshipContractError(f"{field} contains unsupported control characters")
    if len(value.encode("utf-8")) > 16 * 1024:
        raise RelationshipContractError(f"{field} exceeds its UTF-8 bound")
    return value


def require_evidence_excerpt(value: Any, field: str = "evidence_excerpt") -> str | None:
    if value is None:
        return None
    text = require_memory_content(value, field)
    if len(text.encode("utf-8")) > 4 * 1024:
        raise RelationshipContractError(f"{field} exceeds its UTF-8 bound")
    return text


def canonical_profile_key(value: Any, memory_kind: str) -> str:
    key = require_bounded_text(value, "canonical_key", maximum=96).lower()
    allowed = "abcdefghijklmnopqrstuvwxyz0123456789._-"
    if (
        memory_kind not in PROFILE_MEMORY_KINDS
        or not key.startswith(memory_kind + ".")
        or key.endswith(".")
        or ".." in key
        or any(character not in allowed for character in key)
    ):
        raise RelationshipContractError("canonical_key is outside the closed local vocabulary")
    return key


def _language_text(text: str) -> str:
    return " ".join(unicodedata.normalize("NFKC", text).translate(
        str.maketrans({"’": "'", "‘": "'", "ʼ": "'", "‐": "-", "‑": "-"})
    ).casefold().split())


def _phrase_present(text: str, phrase: str) -> bool:
    text, phrase = _language_text(text), _language_text(phrase)
    if not phrase:
        return False
    # Latin-script words require boundaries (now != snow, morgen != morgens).
    # Chinese/Japanese/Korean inflections and compounds do not use word spaces.
    if any("LATIN" in unicodedata.name(character, "") for character in phrase):
        return re.search(r"(?<!\w)" + re.escape(phrase) + r"(?!\w)", text) is not None
    return phrase in text


def _memory_opt_out(text: str) -> bool:
    normalized = _language_text(text)
    return any(re.search(pattern, normalized) for pattern in _PIN_NEGATIVE_PATTERNS)


def _unquoted_text(text: str) -> str:
    masked = list(text)
    for quote in _QUOTED_RE.finditer(text):
        masked[quote.start():quote.end()] = " " * (quote.end() - quote.start())
    return "".join(masked)


def _fact_sentence_spans(visible: str) -> list[tuple[int, int]]:
    result = []
    start = 0
    for separator in list(_SENTENCE_SEPARATOR_RE.finditer(visible)) + [None]:
        end = separator.start() if separator else len(visible)
        normalized = _language_text(visible[start:end]).lstrip(" .:：")
        if normalized and not _NONFACT_SENTENCE_RE.search(normalized) and (
            separator is None or separator.group() not in "?？"
        ):
            result.append((start, end))
        start = separator.end() if separator else end
    return result


def _personal_clauses(text: str) -> list[tuple[int, int, str]]:
    """Keep offsets into player text so a model cannot cut away a quotation.

    This is a bounded lexical screen, not proof that every natural sentence is
    true. Semantic review still rejects roleplay, guesses and irrelevant text.
    """
    visible = _unquoted_text(text)
    result = []
    for sentence_start, sentence_end in _fact_sentence_spans(visible):
        pending_time_start = None
        sentence_result = []
        for clause in _CLAUSE_RE.finditer(visible, sentence_start, sentence_end):
            normalized = _language_text(clause.group()).lstrip(" .:：")
            if not normalized or any(re.search(pattern, normalized) for pattern in _NONPLAYER_PATTERNS):
                pending_time_start = None
                continue
            # Keep fronted time qualifiers attached across a natural comma.
            # A complete neighboring fact does not donate its time qualifier.
            has_predicate = any(
                _phrase_present(normalized, phrase)
                for phrases in _PROFILE_KIND_SIGNAL_PHRASES.values() for phrase in phrases
            ) or any(_phrase_present(normalized, phrase) for phrase in _SCHEDULE_PHRASES)
            if has_temporary_language(normalized) and not has_predicate:
                pending_time_start = clause.start()
                continue
            start = pending_time_start if pending_time_start is not None else clause.start()
            combined = visible[start:clause.end()]
            pending_time_start = None
            self_reference = any(re.search(pattern, normalized) for pattern in _SELF_PATTERNS)
            direct_communication = any(
                _phrase_present(normalized, phrase)
                for phrase in _PROFILE_KIND_SIGNAL_PHRASES["communication"]
            )
            # Subject omission is normal in Japanese/Korean, and in a Chinese
            # personal schedule. Explicit third-person clauses were removed.
            implicit_subject = bool(re.search(r"[\u3040-\u30ff\uac00-\ud7a3]", normalized))
            implicit_schedule = has_temporary_language(combined) and any(
                _phrase_present(normalized, phrase) for phrase in _SCHEDULE_PHRASES
            ) and not re.search(r"他|她|朋友|同事|莉莉|天氣|天气|新聞|新闻", normalized)
            if self_reference or direct_communication or implicit_subject or implicit_schedule:
                sentence_result.append((start, clause.end(), combined))
            elif sentence_result and re.match(
                r"^(?:especially\b|particularly\b|特別是|特别是|尤其是)", normalized
            ):
                # Preserve an ordinary object elaboration, not a new subject.
                prior_start = sentence_result[-1][0]
                sentence_result[-1] = (prior_start, clause.end(), visible[prior_start:clause.end()])
        result.extend(sentence_result)
    return result


def is_explicit_pin_request(text: str) -> bool:
    if not isinstance(text, str) or not text.strip():
        return False
    if _memory_opt_out(text):
        return False
    return bool(_pin_cue_spans(text))


def _pin_cue_spans(text: str) -> list[tuple[int, int]]:
    # Length-preserving normalization keeps permission tied to the actual cue,
    # never the model's arbitrarily enlarged pin_evidence selection.
    visible = _unquoted_text(text).translate(str.maketrans({"’": "'", "‘": "'", "ʼ": "'"}))
    spans = set()
    for start, end in _fact_sentence_spans(visible):
        for pattern in _PIN_AFFIRMATIVE_PATTERNS:
            for cue in re.finditer(pattern, visible[start:end]):
                spans.add((start + cue.start(), start + cue.end()))
    return sorted(spans)


def _pin_authorizes_evidence(witness_text: str, pin_evidence: str, evidence: str) -> tuple[int, int] | None:
    pin_start = witness_text.find(pin_evidence)
    evidence_start = witness_text.find(evidence)
    if pin_start < 0 or evidence_start < 0:
        return None
    pin_end = pin_start + len(pin_evidence)
    evidence_end = evidence_start + len(evidence)
    for cue_start, cue_end in _pin_cue_spans(witness_text):
        if not (pin_start <= cue_start and cue_end <= pin_end):
            continue
        if cue_start < evidence_end and evidence_start < cue_end:
            return (cue_start, cue_end)
        gap = witness_text[cue_end:evidence_start] if cue_end <= evidence_start else witness_text[evidence_end:cue_start]
        punctuation = " \t\r\n，,。.!！?？:：;；—-"
        if (len(gap) <= 8 and not gap.strip(punctuation)) or (
            len(gap) <= 16 and _language_text(gap).strip(punctuation) in {"that", "que", "dass"}
        ):
            return (cue_start, cue_end)
    return None


def has_temporary_language(text: str) -> bool:
    normalized = _language_text(_QUOTED_RE.sub(" ", text))
    normalized = re.sub(r"\b(?:por la mañana|cada mañana|todas las mañanas|jeden morgen|am morgen)\b", " ", normalized)
    return any(_phrase_present(normalized, phrase) for phrase in _TEMPORARY_PHRASES) or any(
        re.search(pattern, normalized) for pattern in _TEMPORARY_PATTERNS
    )


def should_review_profile_text(text: str) -> bool:
    if not isinstance(text, str) or not text.strip() or contains_secret(text) or _memory_opt_out(text):
        return False
    for _, _, clause in _personal_clauses(text):
        if is_explicit_pin_request(clause) or any(
            _phrase_present(clause, phrase) for phrase in _PROFILE_SIGNAL_PHRASES
        ) or any(evidence_explicitly_supports_kind(clause, kind) for kind in PROFILE_MEMORY_KINDS):
            return True
    return False


def _ascii_phrase_present(text: str, phrase: str) -> bool:
    normalized_text = " " + " ".join(
        "".join(
            character if character.isascii() and character.isalnum() else " "
            for character in text.casefold()
        ).split()
    ) + " "
    normalized_phrase = " ".join(
        "".join(
            character if character.isalnum() else " "
            for character in phrase.casefold()
        ).split()
    )
    return " " + normalized_phrase + " " in normalized_text


def evidence_explicitly_supports_kind(evidence: str, memory_kind: str) -> bool:
    if not _personal_clauses(evidence):
        return False
    phrases = _PROFILE_KIND_SIGNAL_PHRASES.get(memory_kind, ())
    if memory_kind == "routine" and has_temporary_language(evidence):
        phrases += _SCHEDULE_PHRASES
    folded = _language_text(evidence)
    evidence_material = "".join(character for character in folded if character.isalnum())
    for phrase in phrases:
        present = _phrase_present(evidence, phrase)
        if not present:
            continue
        phrase_material = "".join(
            character for character in phrase.casefold() if character.isalnum()
        )
        # A bare classifier cue such as "I like" / "我喜歡" is not a fact.
        # The exact player span must also contain a subject value or object.
        if len(evidence_material) > len(phrase_material):
            return True
    return False


def _content_proven_by_evidence(evidence: str, proposed_content: str) -> str:
    """Keep a useful model normalization only when the player span proves it.

    The model is allowed to remove a first-person subject, but it cannot add a
    fact, instruction, or interpretation that is absent from the exact player
    evidence.  When the rewrite is not mechanically provable, the exact
    evidence itself is the safe stored value.
    """

    if proposed_content == evidence:
        return proposed_content
    # A substring can silently drop "not", "cancelled", or a deadline. Only
    # removing one leading first-person subject preserves the whole predicate.
    normalized = re.sub(
        r"^(?:我(?!的)|私(?:は)?|僕(?:は)?|俺(?:は)?|저는\s*|나는\s*|제가\s*|내가\s*|(?i:i\s+|me\s+|my\s+))",
        "", evidence, count=1,
    ).lstrip()
    return proposed_content if proposed_content == normalized else evidence


def compile_profile_evidence(
    evidence: str,
    memory_kind: str,
    *,
    proposed_content: str,
    proposed_assertion_kind: str,
    proposed_retention_days: int | None,
    observed_at: datetime,
) -> tuple[str, str, int | None, str | None]:
    """Compile model-selected evidence into host-authoritative memory fields.

    The model may point at an exact player span and classify it, but its prose
    rewrite and claimed assertion kind never become storage authority.
    """

    content = _content_proven_by_evidence(evidence, proposed_content)
    if _memory_opt_out(evidence) or not _personal_clauses(evidence):
        raise RelationshipContractError("profile evidence must describe the player directly")
    temporary = has_temporary_language(evidence)
    if temporary:
        if not evidence_explicitly_supports_kind(evidence, memory_kind):
            raise RelationshipContractError("temporary profile memory requires complete player evidence")
        if proposed_assertion_kind != "temporary" or proposed_retention_days is None:
            raise RelationshipContractError(
                "temporary player evidence cannot become durable memory"
            )
        retention_days = proposed_retention_days
        return (
            content,
            "temporary",
            retention_days,
            canonical_utc(observed_at + timedelta(days=retention_days)),
        )

    if proposed_assertion_kind == "temporary" or proposed_retention_days is not None:
        raise RelationshipContractError(
            "durable player evidence cannot use temporary retention"
        )
    explicit_supported = evidence_explicitly_supports_kind(evidence, memory_kind)
    if proposed_assertion_kind == "explicit" and not explicit_supported:
        raise RelationshipContractError(
            "explicit profile memory requires complete player evidence"
        )
    assertion_kind = (
        "explicit"
        if proposed_assertion_kind == "explicit" and explicit_supported
        else "inferred"
    )
    return (
        content,
        assertion_kind,
        None,
        None,
    )


def _looks_like_assigned_secret(text: str, marker: str) -> bool:
    lower = text.casefold()
    start = 0
    marker_lower = marker.casefold()
    while True:
        index = lower.find(marker_lower, start)
        if index < 0:
            return False
        tail = text[index + len(marker) : index + len(marker) + 160]
        assigned = re.match(
            r"^\s*(?::|=|：|\bis\b|\bwas\b|\bequals\b|\bes\b|\best\b|\bist\b|\bé\b|是|為|为|は|는|은)\s*(.*)$",
            tail,
            flags=re.IGNORECASE,
        )
        if assigned:
            remainder = assigned.group(1).strip()
            candidate = remainder.split(maxsplit=1)[0] if remainder else ""
            candidate = candidate.strip("'\"`，,。.;；)")
            if len(candidate) >= 4:
                return True
        start = index + len(marker_lower)


def _looks_high_entropy(value: str) -> bool:
    candidate = value.strip("'\"`()[]{}，,。.;；)")
    if len(candidate) < 16 or len(candidate) > 4096:
        return False
    if any(character.isspace() for character in candidate):
        return False
    if any(not (character.isalnum() or character in "._~+/=-") for character in candidate):
        return False
    classes = sum(
        (
            any(character.islower() for character in candidate),
            any(character.isupper() for character in candidate),
            any(character.isdigit() for character in candidate),
            any(not character.isalnum() for character in candidate),
        )
    )
    return classes >= 2 and len(set(candidate)) >= 8


def _has_payment_number(text: str) -> bool:
    digits = ""
    for character in text:
        if character.isdigit():
            digits += character
            if len(digits) > 19:
                digits = digits[-19:]
        elif character in " -":
            continue
        else:
            if 13 <= len(digits) <= 19:
                return True
            digits = ""
    return 13 <= len(digits) <= 19


def contains_secret(text: Any) -> bool:
    """Conservatively reject credential/payment material before persistence."""

    if not isinstance(text, str):
        return True
    folded = text.casefold()
    if any(prefix.casefold() in folded for prefix in _SECRET_PREFIXES):
        return True
    if (
        next(re.finditer(_PEM_PRIVATE_KEY_RE, text), None) is not None
        or next(re.finditer(_SSH_KEY_RE, text), None) is not None
        or next(re.finditer(_TELEGRAM_BOT_TOKEN_RE, text), None) is not None
        or next(re.finditer(_AWS_ACCESS_KEY_RE, text), None) is not None
        or next(re.finditer(_JWT_RE, text), None) is not None
        or next(re.finditer(_BEARER_RE, text), None) is not None
        or next(re.finditer(_BASIC_AUTH_RE, text), None) is not None
        or next(re.finditer(_OAUTH_TOKEN_RE, text), None) is not None
    ):
        return True
    if any(_looks_like_assigned_secret(text, marker) for marker in _SECRET_ASSIGNMENT_MARKERS):
        return True
    for match in re.finditer(_GENERIC_SECRET_ASSIGNMENT_RE, text):
        if _looks_high_entropy(match.group(1)):
            return True
    for match in re.finditer(_HIGH_ENTROPY_ASSIGNMENT_RE, text):
        candidate = match.group(1)
        classes = sum(
            (
                any(character.islower() for character in candidate),
                any(character.isupper() for character in candidate),
                any(character.isdigit() for character in candidate),
                any(not character.isalnum() for character in candidate),
            )
        )
        if classes >= 3 and _looks_high_entropy(candidate):
            return True
    compact = "".join(character for character in text if not character.isspace())
    if compact.count(".") >= 2 and compact.startswith("eyJ") and len(compact) >= 40:
        return True
    return _has_payment_number(text)


def contains_unsafe_memory_instruction(text: Any) -> bool:
    if not isinstance(text, str):
        return True
    normalized = text.casefold()
    return any(phrase.casefold() in normalized for phrase in _UNSAFE_MEMORY_CONTENT_PHRASES)


@dataclass(frozen=True)
class ProfileMemoryAssertion:
    assertion_id: str
    scope: RelationshipScope
    source_hash: str
    session_id: str
    turn_id: str
    user_message_id: str
    assistant_message_id: str
    observed_at_utc: str
    recorded_at_utc: str
    memory_kind: str
    canonical_key: str
    content: str
    evidence_excerpt: str
    assertion_kind: str
    category: str
    source: str
    state: str
    expires_at_utc: str | None
    pinned: bool
    pin_evidence: str | None


@dataclass(frozen=True)
class ProfileMemoryEntry:
    entry_id: str
    entry_version: int
    scope: RelationshipScope
    category: str
    state: str
    content: str
    source: str
    observed_at_utc: str
    updated_at_utc: str
    expires_at_utc: str | None
    pinned: bool
    evidence_excerpt: str | None
    memory_kind: str
    canonical_key: str
    source_hash: str
    current_revision_id: str

    @property
    def value(self) -> str:
        return self.content

    @property
    def durability(self) -> str:
        return "temporary" if self.category == "temporary" else "durable"

    def public_record(self, *, now: datetime | None = None) -> dict[str, Any]:
        state = self.state
        if state == "active" and self.expires_at_utc is not None and now is not None:
            if parse_utc(self.expires_at_utc, "expires_at_utc") <= now:
                state = "expired"
        return {
            "entry_id": self.entry_id,
            "entry_version": self.entry_version,
            "category": self.category,
            "state": state,
            "content": self.content,
            "source": self.source,
            "observed_at_utc": self.observed_at_utc,
            "updated_at_utc": self.updated_at_utc,
            "expires_at_utc": self.expires_at_utc,
            "pinned": self.pinned,
            "evidence_excerpt": self.evidence_excerpt,
        }


@dataclass(frozen=True)
class ProfileTemporaryResolution:
    """A host-bound cancellation proposal, never a model management command."""

    target: ProfileMemoryEntry
    witness: FrozenTurn
    evidence_excerpt: str
    recorded_at_utc: str


# Deliberately finite English/Chinese schedule vocabulary. Different events of
# the same kind remain ambiguous; a model cannot resolve that by inventing ids.
_RESOLUTION_SCHEDULES = {
    "business_trip": ("出差", "business trip", "work trip", "travel for work"),
    "trip": ("旅行", "旅遊", "旅游", "trip", "travel", "traveling", "travelling"),
    "meeting": ("會議", "会议", "開會", "开会", "meeting"),
    "appointment": ("預約", "预约", "appointment"),
    "leave": ("休假", "請假", "请假", "vacation", "leave", "holiday"),
    "exam": ("考試", "考试", "exam"),
    "overtime": ("加班", "overtime"),
    "shift": ("值班", "輪班", "轮班", "shift"),
}
_RESOLUTION_ZH_SUBJECT = r"(?:出差|旅行|旅遊|旅游|會議|会议|開會|开会|預約|预约|休假|請假|请假|考試|考试|加班|值班|輪班|轮班)"
_RESOLUTION_ZH_TIME = (
    r"(?:(?:到|直到)?(?:今天|明天|明日|後天|后天|今晚|這週|这周|下週|下周|"
    r"下(?:個|个)?月|這(?:個|个)月|下星期|下禮拜|下礼拜|週末|周末|"
    r"(?:週|周|星期)[一二三四五六日天]|\d{4}年\d{1,2}月\d{1,2}[日號号]|"
    r"\d{1,2}月\d{1,2}[日號号]|\d{4}-\d{2}-\d{2})的?)?"
)
_RESOLUTION_ZH_PREFIX = r"(?:我(?:的)?|咱(?:們|们)?(?:的)?)?" + _RESOLUTION_ZH_TIME
_RESOLUTION_ZH_PATTERNS = (
    _RESOLUTION_ZH_PREFIX + _RESOLUTION_ZH_SUBJECT
    + r"(?:的)?(?:安排|行程|計畫|计划|計劃)?(?:也)?(?:已經|已经|已)?(?:取消|作廢|作废|終止|终止|中止)(?:了|啦|囉|啰)?",
    _RESOLUTION_ZH_PREFIX + r"(?:不再需要|不需要|不用|不必)(?:再)?(?:去)?"
    + _RESOLUTION_ZH_SUBJECT + r"(?:了|啦)?",
    _RESOLUTION_ZH_PREFIX + r"不(?:再)?" + _RESOLUTION_ZH_SUBJECT + r"了",
    _RESOLUTION_ZH_PREFIX + _RESOLUTION_ZH_SUBJECT + r"(?:不再需要|不用|不必)(?:再)?去(?:了|啦)?",
    r"我(?:已經|已经|已)?取消了?(?:我的)?" + _RESOLUTION_ZH_TIME
    + _RESOLUTION_ZH_SUBJECT + r"(?:的)?(?:安排|行程|計畫|计划|計劃)?(?:了)?",
)
_RESOLUTION_EN_SUBJECT = r"(?:business trip|work trip|trip|travel|traveling|travelling|meeting|appointment|vacation|leave|holiday|exam|overtime|shift)"
_RESOLUTION_EN_TIME = (
    r"(?:\s+(?:for\s+|on\s+)?(?:today|tomorrow|tonight|this week|next week|next month|"
    r"(?:next\s+)?(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)|\d{4}-\d{2}-\d{2}))?"
)
_RESOLUTION_EN_PATTERNS = (
    r"my\s+" + _RESOLUTION_EN_SUBJECT + _RESOLUTION_EN_TIME
    + r"\s+(?:is|was|has been|got)\s+(?:cancelled|canceled|called off)",
    r"i\s+(?:cancelled|canceled|called off)\s+my\s+" + _RESOLUTION_EN_SUBJECT + _RESOLUTION_EN_TIME,
    r"i\s+(?:no longer need to|don't need to|do not need to|won't|will not|am not going to)\s+"
    + r"(?:(?:go on|go to|attend|take|do)\s+(?:a\s+|my\s+|the\s+)?)?"
    + _RESOLUTION_EN_SUBJECT + _RESOLUTION_EN_TIME + r"(?:\s+anymore)?",
    r"i\s+am\s+not\s+(?:traveling|travelling)" + _RESOLUTION_EN_TIME + r"(?:\s+anymore)?",
)
_RESOLUTION_UNCERTAIN_OR_RESTORED = re.compile(
    r"如果|假如|假設|假设|要是|可能|也許|也许|差點|差点|幾乎|几乎|"
    r"恢復|恢复|照常|照舊|照旧|還是|还是|重新|又要|並未|并未|"
    r"(?:沒|没|沒有|没有|未|不會|不会|不能|不想|不打算|不)(?:真的)?取消|取消不了|"
    r"\b(?:if|unless|suppose|imagine|hypothetical|maybe|perhaps|might|would|almost|nearly|"
    r"reinstated|restored|resumed|rescheduled)\b|\bback\s+on\b|"
    r"\b(?:not|never)\s+(?:been\s+)?cancel(?:led|ed)\b|\bno\s+cancellation\b",
    re.IGNORECASE,
)


def _resolution_schedule_anchors(text: str) -> frozenset[str]:
    normalized = _language_text(text)
    business = any(_phrase_present(normalized, phrase) for phrase in _RESOLUTION_SCHEDULES["business_trip"])
    return frozenset(
        kind for kind, phrases in _RESOLUTION_SCHEDULES.items()
        if any(
            _phrase_present(normalized, phrase)
            for phrase in phrases
            if not (business and kind == "trip" and phrase in {"trip", "travel"})
        )
    )


def _temporary_resolution_clauses(text: str) -> tuple[str, ...]:
    """Return whole, affirmative factual clauses; never extract a predicate."""
    if (
        not isinstance(text, str) or not text.strip() or len(text) > 8192
        or contains_secret(text) or contains_unsafe_memory_instruction(text)
        or _memory_opt_out(text) or _QUOTED_RE.search(text)
        or re.search(r"[?？]", text)
        or _RESOLUTION_UNCERTAIN_OR_RESTORED.search(_language_text(text))
    ):
        return ()
    result = []
    for sentence in _SENTENCE_SEPARATOR_RE.split(text):
        for clause in _CLAUSE_RE.finditer(sentence):
            original = clause.group().strip()
            if not original:
                continue
            if len(original) > 480:
                return ()
            normalized = _language_text(original)
            # Unknown surrounding clauses may supply a third-party subject,
            # reported speech, or a later reversal. Do not drop that context
            # to salvage a recognized cancellation substring.
            if not any(re.fullmatch(pattern, normalized) for pattern in _RESOLUTION_ZH_PATTERNS + _RESOLUTION_EN_PATTERNS):
                return ()
            result.append(original)
    return tuple(result)


def has_temporary_resolution_language(text: str) -> bool:
    """Admission only: a matching phrase never authorizes a state change."""
    return bool(_temporary_resolution_clauses(text))


_RESOLUTION_CALENDAR_DATE_RE = re.compile(
    r"(?<!\d)(?:\d{4}-\d{2}-\d{2}|\d{4}年\d{1,2}月\d{1,2}[日號号])(?!\d)"
)
_RESOLUTION_UNKNOWN_TIME = "unknown-relative-or-multiple-time"


def _resolution_time_key(text: str) -> str | None:
    """Recognize one absolute calendar date, never infer a player timezone."""
    normalized = _language_text(text)
    dates = list(_RESOLUTION_CALENDAR_DATE_RE.finditer(normalized))
    remaining = _RESOLUTION_CALENDAR_DATE_RE.sub(" ", normalized)
    if (
        has_temporary_language(remaining)
        or any(match.group() for match in re.finditer(_RESOLUTION_ZH_TIME, remaining))
        or re.search(r"\b(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", remaining)
    ):
        return _RESOLUTION_UNKNOWN_TIME
    if not dates:
        return None
    if len(dates) != 1 or re.search(
        r"(?:到|直到|截至|\buntil|\bthrough|\btill)\s*$", normalized[:dates[0].start()]
    ):
        return _RESOLUTION_UNKNOWN_TIME
    year, month, day = (int(part) for part in re.findall(r"\d+", dates[0].group()))
    try:
        return datetime(year, month, day).date().isoformat()
    except ValueError:
        return _RESOLUTION_UNKNOWN_TIME


def _resolution_time_compatible(clause: str, entry: ProfileMemoryEntry) -> bool:
    requested = _resolution_time_key(clause)
    if requested is None:
        return True
    if requested == _RESOLUTION_UNKNOWN_TIME:
        return False
    existing = {
        key for source in (entry.content, entry.evidence_excerpt or "")
        if (key := _resolution_time_key(source)) is not None
    }
    return existing == {requested}


def temporary_resolution_candidates(
    text: str, entries: tuple[ProfileMemoryEntry, ...], *, observed_at: datetime,
) -> tuple[ProfileMemoryEntry, ...]:
    """Select unique, existing event subjects without guessing between trips."""
    reference = parse_utc(canonical_utc(observed_at), "observed_at_utc")
    clauses = _temporary_resolution_clauses(text)
    anchors = frozenset(
        anchor for clause in clauses
        for anchor in _resolution_schedule_anchors(clause)
    )
    if not anchors:
        return ()
    eligible: list[tuple[ProfileMemoryEntry, frozenset[str]]] = []
    seen_ids: set[str] = set()
    for entry in entries:
        if not isinstance(entry, ProfileMemoryEntry):
            raise RelationshipContractError("temporary resolution candidate type is invalid")
        if entry.entry_id in seen_ids:
            return ()
        seen_ids.add(entry.entry_id)
        if entry.state != "active" or entry.category != "temporary" or entry.pinned or entry.expires_at_utc is None:
            continue
        if (
            parse_utc(entry.expires_at_utc, "expires_at_utc") <= reference
            or parse_utc(entry.observed_at_utc, "observed_at_utc") > reference
            or (
                parse_utc(entry.updated_at_utc, "updated_at_utc") > reference
                # A delayed first automatic review may record an older
                # witnessed fact after its cancellation. Later versions or
                # operator edits retain the strict update-time barrier.
                and not (entry.source == "explicit_user" and entry.entry_version == 1)
            )
        ):
            continue
        subjects = _resolution_schedule_anchors(entry.content + " " + (entry.evidence_excerpt or ""))
        if not any(
            subjects & _resolution_schedule_anchors(clause)
            and _resolution_time_compatible(clause, entry)
            for clause in clauses
        ):
            continue
        # Mixed-event entries cannot be partially cancelled without rewriting
        # the remaining fact, which this operation deliberately does not do.
        if subjects & anchors:
            eligible.append((entry, subjects))
    counts = {anchor: sum(anchor in subjects for _, subjects in eligible) for anchor in anchors}
    result = tuple(
        entry for entry, subjects in eligible
        if len(subjects) == 1 and all(counts[anchor] == 1 for anchor in subjects)
    )
    return result if len(result) <= 8 else ()


def bind_temporary_resolutions(
    parsed: list, *, witness: FrozenTurn, candidates: tuple[ProfileMemoryEntry, ...], recorded_at: datetime,
) -> list[ProfileTemporaryResolution]:
    """Recheck host targets and complete current-player cancellation evidence."""
    if not isinstance(parsed, list) or len(parsed) > 4:
        raise RelationshipContractError("temporary resolutions must be a bounded list")
    if not isinstance(candidates, tuple) or len(candidates) > 8:
        raise RelationshipContractError("temporary resolution candidates are invalid")
    if any(not isinstance(entry, ProfileMemoryEntry) or entry.scope != witness.scope for entry in candidates):
        raise RelationshipContractError("temporary resolution candidate scope mismatch")
    if len({entry.entry_id for entry in candidates}) != len(candidates):
        raise RelationshipContractError("temporary resolution candidates contain duplicates")
    if contains_secret(witness.user_text) or contains_secret(witness.assistant_text):
        raise RelationshipContractError("credential-like turns cannot resolve profile memory")
    observed_at = parse_utc(witness.observed_at_utc, "observed_at_utc")
    recorded_at_utc = canonical_utc(recorded_at)
    if parse_utc(recorded_at_utc, "recorded_at_utc") < observed_at:
        raise RelationshipContractError("temporary resolution recording predates its witness")
    allowed = temporary_resolution_candidates(witness.user_text, candidates, observed_at=observed_at)
    clauses = _temporary_resolution_clauses(witness.user_text)
    bound: list[ProfileTemporaryResolution] = []
    seen: set[str] = set()
    for raw in parsed:
        if not isinstance(raw, dict) or set(raw) != {"target_ref", "evidence", "disposition"} or raw["disposition"] != "cancelled":
            raise RelationshipContractError("temporary resolution has an unsupported shape")
        target_ref = raw["target_ref"]
        if not isinstance(target_ref, str) or re.fullmatch(r"temporary_[0-7]", target_ref) is None:
            raise RelationshipContractError("temporary resolution target is invalid")
        index = int(target_ref.rsplit("_", 1)[1])
        if index >= len(candidates) or target_ref in seen:
            raise RelationshipContractError("temporary resolution target is absent or duplicated")
        target = candidates[index]
        evidence = _single_line_text(raw["evidence"], "evidence", maximum=480)
        if witness.user_text.count(evidence) != 1 or evidence.rstrip("。.!！；;") not in clauses:
            raise RelationshipContractError("temporary resolution requires one complete unique player clause")
        if target not in allowed or target not in temporary_resolution_candidates(evidence, candidates, observed_at=observed_at):
            raise RelationshipContractError("temporary resolution evidence does not identify its target")
        seen.add(target_ref)
        bound.append(ProfileTemporaryResolution(target, witness, evidence, recorded_at_utc))
    return bound


def profile_review_json_schema() -> dict[str, Any]:
    return {
        "type": "object",
        "additionalProperties": False,
        "required": ["memories"],
        "properties": {
            "memories": {
                "type": "array",
                "maxItems": 4,
                "items": {
                    "type": "object",
                    "additionalProperties": False,
                    "required": [
                        "memory_kind", "canonical_key", "content", "evidence",
                        "assertion_kind", "retention_days", "pin_requested", "pin_evidence",
                    ],
                    "properties": {
                        "memory_kind": {"type": "string", "enum": sorted(PROFILE_MEMORY_KINDS)},
                        "canonical_key": {"type": "string", "maxLength": 96},
                        "content": {"type": "string", "maxLength": 240},
                        "evidence": {"type": "string", "maxLength": 480},
                        "assertion_kind": {"type": "string", "enum": sorted(ASSERTION_KINDS)},
                        "retention_days": {
                            "anyOf": [
                                {"type": "null"},
                                {"type": "integer", "minimum": 1, "maximum": 366},
                            ]
                        },
                        "pin_requested": {"type": "boolean"},
                        "pin_evidence": {
                            "anyOf": [
                                {"type": "null"},
                                {"type": "string", "maxLength": 240},
                            ]
                        },
                    },
                },
            }
        },
    }


def bind_profile_review(
    parsed: Any,
    *,
    witness: FrozenTurn,
    recorded_at: datetime,
) -> list[ProfileMemoryAssertion]:
    """Bind a model classification to exact persisted player evidence."""

    if contains_secret(witness.user_text) or contains_secret(witness.assistant_text):
        raise RelationshipContractError("credential-like turns cannot enter profile memory")
    if not isinstance(parsed, dict) or set(parsed) != {"memories"}:
        raise RelationshipContractError("profile review must contain only memories")
    raw_memories = parsed["memories"]
    if not isinstance(raw_memories, list) or len(raw_memories) > 4:
        raise RelationshipContractError("memories must be a bounded list")

    recorded_at_utc = canonical_utc(recorded_at)
    observed_at = parse_utc(witness.observed_at_utc, "observed_at_utc")
    expected_keys = {
        "memory_kind", "canonical_key", "content", "evidence", "assertion_kind",
        "retention_days", "pin_requested", "pin_evidence",
    }
    assertions: list[ProfileMemoryAssertion] = []
    seen: set[str] = set()
    used_pin_spans: set[tuple[int, int]] = set()
    for raw in raw_memories:
        if not isinstance(raw, dict) or set(raw) != expected_keys:
            raise RelationshipContractError("profile assertion has an unsupported shape")
        memory_kind = raw["memory_kind"]
        if memory_kind not in PROFILE_MEMORY_KINDS:
            raise RelationshipContractError("profile memory kind is unsupported")
        canonical_key = canonical_profile_key(raw["canonical_key"], memory_kind)
        proposed_content = _single_line_text(raw["content"], "content", maximum=240)
        evidence = _single_line_text(raw["evidence"], "evidence", maximum=480)
        if witness.user_text.count(evidence) != 1:
            raise RelationshipContractError(
                "profile evidence must be one exact unique span of persisted player text"
            )
        evidence_start = witness.user_text.find(evidence)
        evidence_end = evidence_start + len(evidence)
        if _memory_opt_out(witness.user_text) or not any(
            start <= evidence_start and evidence_end <= end
            for start, end, _ in _personal_clauses(witness.user_text)
        ) or any(
            quote.start() <= evidence_start and evidence_end <= quote.end()
            for quote in _QUOTED_RE.finditer(witness.user_text)
        ):
            raise RelationshipContractError("profile evidence is quoted, hypothetical, or not player-authored")
        if not has_temporary_language(evidence) and any(
            start <= evidence_start and evidence_end <= end and has_temporary_language(clause)
            for start, end, clause in _personal_clauses(witness.user_text)
        ):
            raise RelationshipContractError("profile evidence cannot omit its temporal qualifier")
        if contains_secret(proposed_content) or contains_secret(evidence):
            raise RelationshipContractError("profile assertion contains credential-like material")
        if (
            contains_unsafe_memory_instruction(proposed_content)
            or contains_unsafe_memory_instruction(evidence)
        ):
            raise RelationshipContractError("profile assertion contains instruction-like material")

        proposed_assertion_kind = raw["assertion_kind"]
        if proposed_assertion_kind not in ASSERTION_KINDS:
            raise RelationshipContractError("assertion_kind is unsupported")
        proposed_retention_days = raw["retention_days"]
        if proposed_retention_days is not None and (
            isinstance(proposed_retention_days, bool)
            or not isinstance(proposed_retention_days, int)
            or not 1 <= proposed_retention_days <= 366
        ):
            raise RelationshipContractError("retention_days is outside its closed bound")

        content, assertion_kind, retention_days, expires_at_utc = compile_profile_evidence(
            evidence,
            memory_kind,
            proposed_content=proposed_content,
            proposed_assertion_kind=proposed_assertion_kind,
            proposed_retention_days=proposed_retention_days,
            observed_at=observed_at,
        )
        if assertion_kind == "temporary":
            category = "temporary"
            source = "explicit_user"
        elif assertion_kind == "explicit":
            category = "long_term"
            source = "explicit_user"
        else:
            category = "candidate"
            source = "inferred"

        pin_requested = raw["pin_requested"]
        if not isinstance(pin_requested, bool):
            raise RelationshipContractError("pin_requested must be boolean")
        pin_evidence = raw["pin_evidence"]
        if pin_requested:
            if assertion_kind != "explicit":
                raise RelationshipContractError("only an explicit durable memory can be pinned")
            pin_evidence = _single_line_text(pin_evidence, "pin_evidence", maximum=240)
            if witness.user_text.count(pin_evidence) != 1 or not is_explicit_pin_request(pin_evidence):
                raise RelationshipContractError("pin requires exact explicit player authorization")
            pin_span = _pin_authorizes_evidence(witness.user_text, pin_evidence, evidence)
            if pin_span is None or pin_span in used_pin_spans:
                raise RelationshipContractError("pin authorization is not bound to this memory evidence")
            used_pin_spans.add(pin_span)
            category = "relationship_core"
        elif pin_evidence is not None:
            raise RelationshipContractError("pin_evidence is only valid with pin_requested")

        semantics = {
            "memory_kind": memory_kind,
            "canonical_key": canonical_key,
            "content": content,
            "evidence": evidence,
            "assertion_kind": assertion_kind,
            "category": category,
            "expires_at_utc": expires_at_utc,
            "pinned": pin_requested,
            "pin_evidence": pin_evidence,
        }
        semantic_key = canonical_json(semantics)
        if semantic_key in seen:
            raise RelationshipContractError("duplicate profile assertion")
        seen.add(semantic_key)
        assertion_id = sha256_hex(
            "lilith-profile-assertion-v1\0" + witness.source_hash + "\0" + semantic_key
        )
        require_event_identity(assertion_id, "assertion_id")
        assertions.append(
            ProfileMemoryAssertion(
                assertion_id=assertion_id,
                scope=witness.scope,
                source_hash=witness.source_hash,
                session_id=witness.session_id,
                turn_id=witness.turn_id,
                user_message_id=witness.user_message_id,
                assistant_message_id=witness.assistant_message_id,
                observed_at_utc=witness.observed_at_utc,
                recorded_at_utc=recorded_at_utc,
                memory_kind=memory_kind,
                canonical_key=canonical_key,
                content=content,
                evidence_excerpt=evidence,
                assertion_kind=assertion_kind,
                category=category,
                source=source,
                state="active",
                expires_at_utc=expires_at_utc,
                pinned=pin_requested,
                pin_evidence=pin_evidence,
            )
        )
    return assertions


__all__ = [
    "ASSERTION_KINDS", "MANUAL_ADD_CATEGORIES", "MEMORY_CATEGORIES",
    "MEMORY_SOURCES", "MEMORY_STATES", "PROFILE_AUXILIARY_TASK",
    "PROFILE_DATABASE_FILE", "PROFILE_MEMORY_KINDS", "PROFILE_STORE_SCHEMA_VERSION",
    "ProfileMemoryAssertion", "ProfileMemoryEntry", "ProfileTemporaryResolution", "WRITABLE_CATEGORIES",
    "bind_temporary_resolutions", "has_temporary_resolution_language", "temporary_resolution_candidates",
    "bind_profile_review", "canonical_profile_key", "compile_profile_evidence", "contains_secret",
    "contains_unsafe_memory_instruction",
    "evidence_explicitly_supports_kind", "has_temporary_language",
    "is_explicit_pin_request", "profile_review_json_schema",
    "require_evidence_excerpt", "require_memory_content", "should_review_profile_text",
]
