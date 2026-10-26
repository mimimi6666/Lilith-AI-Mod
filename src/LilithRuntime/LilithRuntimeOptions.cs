namespace LilithRuntime;

public sealed record LilithRuntimeOptions
{
    public const string PinnedHermesCommit =
        "3c5fd918e3e2537cd74f4f88c990c5de5cbd9f63";

    public const string PinnedHermesShortCommit = "3c5fd918";

    public const string PinnedHermesComponentVersion =
        "v1-win-x64-cp311-hermes-0.20.1-3c5fd918";

    public const string PinnedCuaDriverSha256 =
        "94216FD10A101A165EFB94E1C30E6ED05E3947AE7140F096B3545B8BDAE580DA";

    public const string PinnedVoiceComponentVersion =
        "v3-win-x64-cp311-hermes-0.20.1";

    public const int PinnedVoiceComponentEntryCount = 2289;

    public const int PinnedVoiceComponentFileCount = 2066;

    public const string PinnedVoiceComponentClosureSha256 =
        "130ECF3B2818829AF22774696231CF868308581184EE50B8100D1E52F9F89656";

    public const string PinnedVoiceModelRevision =
        "ebe41f70d5b6dfa9166e2c581c45c9c0cfc57b66";

    public const string PinnedIdentityFingerprintSha256 =
        LilithIdentityPackageV9Loader.IdentityFingerprintSha256;

    public const string PinnedIdentityPackageManifestSha256 =
        LilithIdentityPackageV9Loader.ManifestSha256;

    public const string PinnedRelationshipMemoryPluginManifestSha256 =
            "489D9857810A82EFE858DE94E4AFF7F445426B3C19889F756ED67D84C770FCCB";

    public const string PinnedNotesPluginManifestSha256 =
        "E3E9F3601E364619B8F53188A2992F74237EA2016139047A7CD42F404BE75E47";

    public const string PinnedLeaveNotePluginManifestSha256 =
        "B98DF5967F4F1B7C681F9D9087B9C72ABFF278D4C0D2FD8BFC533603CDAF8232";

    internal static IReadOnlyDictionary<string, string> PinnedHermesPatchStack
        { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["packaging/hermes/patches/0164-old-maid-playful-bluff.patch"] =
                "408FA54C023A583574CF337AA4F74639E26736143CD7A08C052E61B19C748BC5",
            ["packaging/hermes/patches/0163-minigame-plain-context.patch"] =
                "99DBEE20538B43F81B884E0AD1193006B3ADA87D7D8DDA4ABDB3C63700BCDDE4",
            ["packaging/hermes/patches/0162-minigame-answer-merge.patch"] =
                "9D0D1F04F528179CE6C9C30C0DFBDB4736A602142823ED15AFB853A77B5125DD",
            ["packaging/hermes/patches/0161-minigame-answer-natural.patch"] =
                "E3BE7C76A35D7E0C9377A48B608E88A003484A87A597C461775F4889792C101C",
            ["packaging/hermes/patches/0160-minigame-answer-judge.patch"] =
                "844F1A5E67477A1D90DC9B966826CD293C0D7004858D66D67F0ED0572D6DAD6F",
            ["packaging/hermes/patches/0159-minigame-answer-repair.patch"] =
                "95D46691EF9BB75F543F0557E0BD80C3188AACE19F83780DD433A8DB5BB36EE0",
            ["packaging/hermes/patches/0158-minigame-stream-wire-diagnostic.patch"] =
                "A8AD440AC389F9573A62D46663BB00E8559AE3B6BBFF32D6D948FC3A64D664FC",
            ["packaging/hermes/patches/0157-minigame-wire-diagnostic.patch"] =
                "435CB4A2BD35B6E7F7016C0C1B9C0B51DCA8E2F0C45B2136A486C73D664F0A6C",
            ["packaging/hermes/patches/0156-minigame-context-diagnostic.patch"] =
                "B6FE43322CE3392900A81DF30FFC100A95C703E30E1007B04D6DD29188F8BF77",
            ["packaging/hermes/patches/0155-minigame-lilith-view.patch"] =
                "F25165F2BBC3991315845B8BC1D72FDAF823E3A902A69E3FDC0FD71186ECFC2A",
            ["packaging/hermes/patches/0154-minigame-public-context.patch"] =
                "3BDCFF94FB25BC5ACDD20D46B653E2FC0E804EAA6CF5C940152E161120261660",
            ["packaging/hermes/patches/0153-context-checkpoint-persistence.patch"] =
                "2BAC1EA2D0453A3CED1C5104C7B5FFC9F7F778CB04BEB437420402D95F9F3636",
            ["packaging/hermes/patches/0152-context-economy-recovery.patch"] =
                "79943AB3E2F8AB5BE101C2872DB92B1736AC6BA117D9810CD849DAA83868814A",
            ["packaging/hermes/patches/0151-scene-inference-diagnostics.patch"] =
                "A3C8C24C6BF48EFA717A47F380313C74092B254BA5C7ABE3FAF3369C86A01CE9",
            ["packaging/hermes/patches/0150-local-compatibility-retirement.patch"] =
                "C508462EA583B1FF7BE9BD0B822729E93A4F09E10C69AEBE8544491F1063063F",
            ["packaging/hermes/patches/0149-local-main-request-fit.patch"] =
                "B3F2B693EF9A327BA17DB7040193614295C7209BA683ABEF4B1E4B652C2B349A",
            ["packaging/hermes/patches/0148-local-summary-wire-budget-v2.patch"] =
                "275FA3B89EEBB479C79C42CF73AB2ACD36FE0C46039DD4519E8C4CEE5097E1F0",
            ["packaging/hermes/patches/0147-old-history-recovery.patch"] =
                "3CC6941E998E98DDD63E5F8D708E2D0D36E3CE8F7D93233AA043ADEB6B8A0071",
            ["packaging/hermes/patches/0146-local-compaction-display-history.patch"] =
                "AE281FDDB2EFA3DD27307C3D81183F8F86D7DEF138B9869592018451E1D8F986",
            ["packaging/hermes/patches/0145-local-compatibility-summary-wait.patch"] =
                "5ED1EDA4C62AC3A9DBA86A16017FBE520A91326D315B311F03505B9E757AF8A9",
            ["packaging/hermes/patches/0144-local-compatibility-summary-quality.patch"] =
                "BDA5ED4EC5DE07E32A22BCE6C34B0B0A3170CDAE925B411905D2E47A5F2A20E2",
            ["packaging/hermes/patches/0143-local-compatibility-cancel.patch"] =
                "E61C78A40DA6A58E09F5D043A2491F2A1C9B354F00BBE2E902F313FFD5A02874",
            ["packaging/hermes/patches/0142-local-compatibility-accounting.patch"] =
                "A515B69DA57DE126DE651F3FC3F92E4C768B33BBFEC1905EF3A687412EF7C298",
            ["packaging/hermes/patches/0141-local-compatibility-first-turn.patch"] =
                "C50190C36C046A389E35C8B77B4386AC5C791E4FA08CEA9C26EF08C369ACCA3B",
            ["packaging/hermes/patches/0140-local-compatibility.patch"] =
                "0C4D3218EEA1A0BEF437F90DE88A7B7A33864E21F09BCD588B3B9321A6627B7E",
            ["packaging/hermes/patches/0139-local-provider-session-repair.patch"] =
                "946526CE469184675C69DE2BB2593F06CD81E60E232382C48D902055724F49AD",
            ["packaging/hermes/patches/0138-session-activation-repair.patch"] =
                "C4511E0A41782438E273BCA009061382F18EAD420759A5AD00DD74FB459134AC",
            ["packaging/hermes/patches/0137-previous-state-recovery.patch"] =
                "86151090B541171CEA12AD3C78DF1708DB582B7B2779D31B9B47BFABB9A7A75C",
            ["packaging/hermes/patches/0136-provider-regions.patch"] =
                "ED09EB69C2E990B3BE74AA77A403385D724C601C0A14581B547D60958169C8E1",
            ["packaging/hermes/patches/0135-native-model-transition-recovery.patch"] =
                "46FC468F41E921B4BCD48242E86257D74D5D042A62E5F017C4B2CEEC466C8E88",
            ["packaging/hermes/patches/0134-proactive-scene-delivery.patch"] =
                "CDC3FC70250071B63D9455E8F41338A732AC49B9D1D98C7B9BC64509BB9E7628",
            ["packaging/hermes/patches/0133-provider-setup-references.patch"] =
                "9AED4CF2FF7E9B6CD43AC3C270BFB064D2CF41D22B8D36BF710B18C89B3B0CD8",
            ["packaging/hermes/patches/0132-native-provider-setup.patch"] =
                "5C313FBD2684B28C41FCD2F06D68FB361703771FB6E5B45C0E94EB719F2C2F55",
            ["packaging/hermes/patches/0131-local-image-format.patch"] =
                "9C1FD68E585522409B449B27763D99E946F8E64F3F3133197B7A4FF742F3D1C6",
            ["packaging/hermes/patches/0130-six-level-stability.patch"] =
                "319E37C5E8B2C754A0180A86092580ED5F473331A5E7DD90331131B21B61A538",
            ["packaging/hermes/patches/0129-pending-fixes-batch.patch"] =
                "61A0B2433D9598EAB5798C0FEE7CA342D0CAA8E7FD8FB6D79C868B292C7E2BDF",
            ["packaging/hermes/patches/0128-six-level-strategies.patch"] =
                "1786D6CC64619DF292F7148B1105993F938D7C935C74EFFBDFB4CEF325986EE7",
            ["packaging/hermes/patches/0127-tool-modes-speech-recovery.patch"] =
                "6D40F78E3A5448DC50BE2E184C5CA94E3DA665C6BE41E893AA529AE03BFFF4D6",
            ["packaging/hermes/patches/0126-proactive-continuation.patch"] =
                "07C3EA203C27AB1545EF21273B2F062F65E83F45520E22120CB80B945FD560F1",
            ["packaging/hermes/patches/0125-speech-first.patch"] =
                "4E66830BD2E49F2F02B369EFC8858C1D42CDD731652EC4AD4E38E2E924587205",
            ["packaging/hermes/patches/0124-speech-prompt-compact.patch"] =
                "A1046C7EFD29EDB41D570E934D4615B8462E97388B7CDA05DD523873ABD41C40",
            ["packaging/hermes/patches/0123-speech-boundary-observation.patch"] =
                "185A4BB1C99A50C6915DC43321C3E88D8478F1208E83F54B7C2ACA148832318E",
            ["packaging/hermes/patches/0122-speech-output-contract.patch"] =
                "803D672A99626F1E401D081A4B90F15551B8926233A7DC85732AF8A3D91151C2",
            ["packaging/hermes/patches/0121-speech-wire-observation.patch"] =
                "5CB061A2F8BF58E847CE2B8CE914707A017F4D5DDC51DBC6B87F1803DC1CC259",
            ["packaging/hermes/patches/0120-background-cache-handoff.patch"] =
                "22FBB8FD6D4C3BC7043CF948D70DA8832896C566419DC3FCAB66E50CA1528D7F",
            ["packaging/hermes/patches/0119-speech-pairing-reliability.patch"] =
                "98470D289BC966C6DEDCAAAF510A3D419BC85D566153A96C4970D4426A1D0525",
            ["packaging/hermes/patches/0118-japanese-expressive-delivery.patch"] =
                "52FA3B2276A5AA8BE65F513E8F17DE879665C1AABF46ECAFF9F1C3C23020BAC9",
            ["packaging/hermes/patches/0117-background-review-repair.patch"] =
                "4BADB61DE15C85ACEF9E1D3E0452ACF0A230DDD7EE7F10AED998E80A2BD1A62E",
            ["packaging/hermes/patches/0116-context-cache.patch"] =
                "8720BCD3A6E7A5E9431569B72C6D109F45F14D12C5D03635857CD867B338B1EB",
            ["packaging/hermes/patches/0115-local-scene-repair.patch"] =
                "865C7411038E3258DE39ECDA4602705949FC2D9213CE2C11B0CA424A70DF81EB",
            ["packaging/hermes/patches/0114-local-scene-opt-in.patch"] =
                "99DBE699A2E6C5DF519E924B738BCE7A586DD05971FFEE5F34DFD036DED48714",
            ["packaging/hermes/patches/0113-local-scene-context.patch"] =
                "4C8522358F1B5E3CDD1B1F51EE99C524C71F3C44B4568424BA391B503896FA59",
            ["packaging/hermes/patches/0112-computer-use-coordinates.patch"] =
                "F17798EB9AB0751D0A3DD5878D26D93912B172794B8DA3447366A299FC03A430",
            ["packaging/hermes/patches/0111-general-tool-latency.patch"] =
                "F2D1A4FA9C32944D242456EE059E2CFB4BE79EBDA5A1E31C22EA5060A238115C",
            ["packaging/hermes/patches/0110-tool-efficiency.patch"] =
                "470635D66DD47091AF7D2241555A7F2C1D0AE11852911F51B8347B72EAE893A0",
            ["packaging/hermes/patches/0109-chat-context.patch"] =
                "B0BE502EB4937B60D8F1D260093C021ACF959BD37316AFEC7232BABC5977D257",
            ["packaging/hermes/patches/0108-paired-speech.patch"] =
                "84E2D17658443C9EF44AD4ECCD2229D5A111E7DBCE69DF0BBA3C6EA732BF2152",
            ["packaging/hermes/patches/0107-speech-origin.patch"] =
                "905BDCA13B0F96B9F1F007BF794D359174F5DC5BF86354C72DD205AFF030EDDF",
            ["packaging/hermes/patches/0106-relationship-review.patch"] =
                "2319F22872E615AA886B05426DBFEBAFD6AC140ABF534DB1B4A6A99D9CC57B7E",
            ["packaging/hermes/patches/0105-usage-strategies.patch"] =
                "47EC11623836B5EA658BF0BC3B6726998DF92D53458E7473556BD15A04BB3F5F",
            ["packaging/hermes/patches/0104-usage-budget-read-repair.patch"] =
                "3453C6ED5F8DA056B1FE373EDE8A0A305AB8AFBA7744034CBDEF4FE3DDC25C9B",
            ["packaging/hermes/patches/0103-usage-budget.patch"] =
                "22F0EFCC871647117BF3BF5C5B5651D0A21E0082DA4F9130DDEE1A52375BCD43",
            ["packaging/hermes/patches/0102-google-image-wire-format.patch"] =
                "E801322CF0A745E47B40D16704B33F18DB23792B36781C2482FC48E8B1CCACCA",
            ["packaging/hermes/patches/0101-google-official-image-generation.patch"] =
                "99596AFE44B148FA02618F2CCC70FD884251BFE24E25E4CEA576A81C7F6FC97C",
            ["packaging/hermes/patches/0100-tools-setup-readiness-and-lifecycle.patch"] =
                "47AA8882CDDDDCFB2CD916662798E12C93587845FE1C2DF71471620C2C83A372",
            ["packaging/hermes/patches/0099-deepseek-exact-model-catalog.patch"] =
                "B38FFB9778799B74EB10671ADF99723FD4448CF9EECB0D1722386B1E4DC39D43",
            ["packaging/hermes/patches/0098-managed-sethome-receipt-guard.patch"] =
                "7DB1A21ECDF30BADB8A7BD6C181FD9ED3961344737BDB9EBDAB8F538446D938D",
            ["packaging/hermes/patches/0097-resume-durable-replay.patch"] =
                "0F14C00A2C1B1C63C47714BFB2D080C110B9455528786C2D70C587630921D21E",
            ["packaging/hermes/patches/0096-shared-player-display-name.patch"] =
                "7545B98126E9A8E31C9972350297BFA44A8B2A234C6C4047D81702174C2F0F7A",
            ["packaging/hermes/patches/0095-native-extension-durable-replay.patch"] =
                "0AA2448A43345BD39DF64422ED4DA6757A98B03835405AA026FD5C6524424476",
            ["packaging/hermes/patches/0094-exact-global-model-selection.patch"] =
                "55C14767968F4212F0EF2A7D43C7CF8162AF7A2209D3847DA3D424BE99B8F180",
            ["packaging/hermes/patches/0093-speech-examples-expression.patch"] =
                "6793CBEFB3D5E7C90D4836AD7127D7E24F628B9FDE4CB5F0783D023F78C102D8",
            ["packaging/hermes/patches/0092-original-experiences-expression.patch"] =
                "501E76022B82840DB94C13BE616E7F1FC867CE6EA724998D81F7D849CC0A78A2",
            ["packaging/hermes/patches/0091-tulpa-role-world-expression.patch"] =
                "C8C090D8A5492C987497D1E1841BC3114DE8C4CB232E6034952D312E66E2A311",
            ["packaging/hermes/patches/0090-manual-expression-hot-update.patch"] =
                "4B93E9D7F3BF7AED0ED520C78ACEEA43C17A47E3FA9078E2AF5CAEEF08266FAA",
            ["packaging/hermes/patches/0089-native-media-model-selection.patch"] =
                "4F629631DC740D49234DFDBB8D89A0E77FF3201F9BBBB77023232C6707274975",
            ["packaging/hermes/patches/0088-control-center-send-sound-preference.patch"] =
                "645EA9F68BCD7484C5D214BC8D9738426B553AB281A3CB593026FF8E6CA24BB1",
            ["packaging/hermes/patches/0087-telegram-bounded-speech.patch"] =
                "DCDD1CA470E13C81212AFE5604BBE75DD545863C4AE7E2C7FC8A363613680C35",
            ["packaging/hermes/patches/0086-telegram-speech-language-validation.patch"] =
                "2FDD3A3028817EA9C5C4E882C44A51DDE73564367A62062B26984EC4432DF504",
            ["packaging/hermes/patches/0085-telegram-voice-language-follow.patch"] =
                "FB1F8ACCFDFADC17497AEA461A81D54D788B9A39F57EC0F7F12BBF00003BC076",
            ["packaging/hermes/patches/0001-explicit-empty-cli-toolsets-disable-all-tools.patch"] =
                "46B73286264D413A2AB19E5AC6F7759F81197EE6BDB61F00EF9875B836A4427B",
            ["packaging/hermes/patches/0002-close-failed-turns-durably.patch"] =
                "3F8DC969740D330CD4652D34FAB65DF8BCFEE6D389DDFE5FC72B6286809C4292",
            ["packaging/hermes/patches/0003-gate-headless-computer-use-through-native-approval.patch"] =
                "DC1F7594B39F69448EBBAB032C761577C731D2280667906580A547AFFC6C3B20",
            ["packaging/hermes/patches/0004-preserve-shared-session-tool-resources.patch"] =
                "2151601A1E2B3111314137D1167E00A996B88B8871F60D615C89D0EFAA7176E8",
            ["packaging/hermes/patches/0005-bound-child-capabilities-and-session-controls.patch"] =
                "810003D777323775F8DAEB5AE1E0D2C4FB562F746F6274B0ADFDA57B434D2421",
            ["packaging/hermes/patches/0006-recover-gemini-malformed-function-calls.patch"] =
                "B5C5F380C311F1F4CBC64F4CB1D7516569F90F7B57E5D45CFC48A43088B319C1",
            ["packaging/hermes/patches/0007-harden-checkpoint-rollback-safety.patch"] =
                "1276689380731A6A76562E5106B978E8C2BA812EC8CDF25DE2A345BF51BD09BF",
            ["packaging/hermes/patches/0008-enforce-profile-scoped-identity-lock.patch"] =
                "4515BA635171687084AE2E7B7B7147BE290E7EC585A280DDBDEAD66E345B43F1",
            ["packaging/hermes/patches/0009-expose-native-workbench-tool-controls.patch"] =
                "88A6BBE0C2F22F80C0629C182DB49CBA9618492AA62CE58DB325BB962B4FAA5E",
            ["packaging/hermes/patches/0010-harden-native-config-authority-and-receipt-permanent-approvals.patch"] =
                "67077F1C0670A9F28FBE8AFA0276DE8775F3D72ECE53691A3C875FF46A0CA781",
            ["packaging/hermes/patches/0011-expose-generic-registered-application-launch.patch"] =
                "09CA455D5515A6A8D7136B3941D41A49E480246BFFEC6BBB971D7056883985B8",
            ["packaging/hermes/patches/0012-enforce-generic-application-launch-routing.patch"] =
                "77D05E4E25EC19F0AC670FE739847CC8973EF30B7D07409D5C264A1E495E3B88",
            ["packaging/hermes/patches/0013-preserve-reasoning-event-semantics.patch"] =
                "04BE6621679F336ABD79F43521931B1A1C7C00FC0BBE856849E16FAB4FDAA830",
            ["packaging/hermes/patches/0014-stabilize-native-turn-identity-and-session-projection.patch"] =
                "B94C31061323CD1B4821DB33A93949370C82234DE5AB780FD49007A0CA93201D",
            ["packaging/hermes/patches/0015-preserve-legacy-turn-only-history-identity.patch"] =
                "E04C5B326B1AFD22CCCB903DD87F074FA7511CC38E97F2F00BF93E2B33BB483F",
            ["packaging/hermes/patches/0016-complete-standalone-turn-identity-claims.patch"] =
                "C4EA7DC326A9A36016B389A5838A7E59344D0D561C877E37F517452116B99C69",
            ["packaging/hermes/patches/0017-expose-host-owned-llm-to-memory-provider-plugins.patch"] =
                "DE0857AFEB95053B4CED03FDA90AA0E6D6FE65BE6AAAD4940DC8A54F740AFE74",
            ["packaging/hermes/patches/0018-pin-managed-config-integrity.patch"] =
                "BC1C1AFA8CF4EA0241207F708BB7F273CF080CE1C725FF95BBEE8460766FFF7D",
            ["packaging/hermes/patches/0019-apply-pinned-managed-config-to-strict-agents.patch"] =
                "D8318B224E540B3A7D9D3F69A28288E3F1B554F6FDB30973E257872AFD0A0842",
            ["packaging/hermes/patches/0020-bind-strict-auxiliary-routes-and-scope-persisted-memory.patch"] =
                "68C13F11D65D68ABB3E1F964CFF7932EEECAA61D557FC2DBB7F15C191A9846D7",
            ["packaging/hermes/patches/0021-expose-native-provider-api-key-lifecycle.patch"] =
                "C753D3B97F1780A19019366260040EBD0D0AA4A23CA0D565E80FD4EFF170060C",
            ["packaging/hermes/patches/0022-bind-authenticated-provider-model-default.patch"] =
                "9D985714EB8A1B3D199389A6768C4DB8F5DAC398ADE5086BDF7B779F5FC12809",
            ["packaging/hermes/patches/0023-persist-primary-assistant-identity-before-incremental-flush.patch"] =
                "BA18B92423A086C67B55F78580506D21C1CCB5FDD02DF925D8BA6E96EEA55066",
            ["packaging/hermes/patches/0024-expose-prompt-only-structured-plugin-completions.patch"] =
                "E88BBAD8EDDFD480D6C41943F48F8D10F143850B2DB84A9AC4EF54A9E25457B0",
            ["packaging/hermes/patches/0025-bound-evidence-based-background-learning.patch"] =
                "D0D2142B55B9E908D1C11C1620E76925CD7373F806842DB2B0A7EFA957AED725",
            ["packaging/hermes/patches/0026-use-prompt-only-session-title-generation.patch"] =
                "89D46B86E95389BFA0AE52A396BBB919BCA8B693FD6741912BF468B69D5A093B",
            ["packaging/hermes/patches/0027-preserve-returned-error-assistant-partials.patch"] =
                "354239D18D4FF73B2AAFBBC05C7047ED72BF55AFEA1ADD8D766A582F933CCE62",
            ["packaging/hermes/patches/0028-keep-emergency-stop-responsive-and-preserve-history-time.patch"] =
                "D52D67A6A6D68DA94C0048304A204CF3ECD63B9977786F0ACE865BFCAD0C0698",
            ["packaging/hermes/patches/0029-publish-stale-interrupt-session-info.patch"] =
                "6EC8D0BFF77330FB3006D6963DAFF0FD9F5BB09FD028517BF73A4F768802DFD6",
            ["packaging/hermes/patches/0030-route-semantic-memory-before-transcript-search.patch"] =
                "A4EB2D65A2D7451ABD10304C9403C77517A22681D943309042F270ED8A13C58F",
            ["packaging/hermes/patches/0031-project-structured-response-format-to-native-gemini.patch"] =
                "17E36FD4D153DDB46414CBA96ABA37D35F1A636B51818A0879F2F8948E0D3AB0",
            ["packaging/hermes/patches/0032-keep-internal-summary-nudges-out-of-user-history.patch"] =
                "700A7E5F480D899F073E402B184A4183F903A4EEB14496916702CCFD157129AC",
            ["packaging/hermes/patches/0033-create-hidden-durable-sessions.patch"] =
                "C323FC1573A9ABFEA6925883007CD9658A382AAD260FC3318C3D247935A989AA",
            ["packaging/hermes/patches/0034-attest-durable-hidden-session-lineage.patch"] =
                "1F95C5C4C5DD61177D8F89E182176FAC139C4604816FAF727A58D2AEFD05C4AD",
            ["packaging/hermes/patches/0035-expose-bounded-per-capture-native-voice.patch"] =
                "77D87386A1D11804F9D4EF877FB9BFE866A95DF7370BF51732F59D9D708BD6D6",
            ["packaging/hermes/patches/0036-support-multilingual-game-voice-capture.patch"] =
                "AE962FF2D9CB54A692F0928FC0DB803FFD1308ED878C3DD428666D3BF9641370",
            ["packaging/hermes/patches/0037-linearize-deferred-model-authority.patch"] =
                "C612EC083E6500337E9DF77566957F3E5FD58A2A8122E41469112E6798B6612F",
            ["packaging/hermes/patches/0038-durable-conversation-activity.patch"] =
                "98158433ED436B440513D0BDF5D91652B2E0E1B6A5698ADF23707BDBD728A4AC",
            ["packaging/hermes/patches/0039-repair-durable-conversation-activity-projection.patch"] =
                "8C2694CB1EBB0ECD605A858AE1B9484DD25A3C115B36978FFF825931B5D1187E",
            ["packaging/hermes/patches/0040-fix-deferred-session-info-usage.patch"] =
                "9F96FC35439C80DF0145A53078E4170875F3F43FB010D8A61D871C3DB2A14E3F",
            ["packaging/hermes/patches/0041-reuse-live-unpersisted-drafts.patch"] =
                "96CE109417F14D3022DA16D416F2657DC450B5879FC6BB3408C32176F8B3AA58",
            ["packaging/hermes/patches/0042-terminalize-deferred-agent-build-exits.patch"] =
                "E8E55984E78A511B15CF137510B3576BC25437D2D7156063FECEDD5056587AEC",
            ["packaging/hermes/patches/0043-finalize-native-config-transitions.patch"] =
                "0569A6BB7F36FB83763E3078BD89546F5C0C8725621E5206F42AE6FB02220E0B",
            ["packaging/hermes/patches/0044-route-game-display-and-speech-locales.patch"] =
                "6F46E79B862BE5C019B737ECFDC54801E7B02AAAA583CA198DB9A161DECC0685",
            ["packaging/hermes/patches/0045-make-speech-projection-provider-portable.patch"] =
                "B39FA9E8620D82F1B7729D4EA65E05DC2E99FA85AA61BBD9192A4EB462EEEB71",
            ["packaging/hermes/patches/0046-expose-read-only-skill-offer-inventory.patch"] =
                "5097EB86C4DB74078340B2DE16668D08E958634855E878913979A6C62AD27AE4",
            ["packaging/hermes/patches/0047-harden-native-tool-readiness-and-approvals.patch"] =
                "E92D8DB3C2DB3D1A90B5D09AC5A2BA8C5C15DA963B34CA4B538421E5925C9FE6",
            ["packaging/hermes/patches/0048-stabilize-local-cron-and-windows-sapi-tts.patch"] =
                "B14057296B159A8E137C399A817F0C9F24C1D52D85B5FA181A4870209112059D",
            ["packaging/hermes/patches/0049-recognize-lilith-runtime-cron-workbench-origin.patch"] =
                "4373B8EDBD6A11B237AB2F9F736C6170E32A0BCB9AAA4E38D0E22225082BABE1",
            ["packaging/hermes/patches/0050-project-cron-completion-into-workbench-timeline.patch"] =
                "DB6F55F551447269FB8C53232EB596B1F9CF38162CB6DD4AD70973A2BBABDD98",
            ["packaging/hermes/patches/0051-refresh-game-tool-authority-and-cron-return.patch"] =
                "521C70E06A75DCD39A6EA8211A87038D3A7A933FFB152711130844D24B03F02B",
            ["packaging/hermes/patches/0052-append-lilith-native-assistant-events.patch"] =
                "2C851A5E6E7ADEF7A7AE7C175AEB1BDFCEFB92B4B89102C126202179313F04F9",
            ["packaging/hermes/patches/0053-extend-lilith-native-assistant-events.patch"] =
                "ACD464E0E2C6826AC594D6A90F20430E3B75D659B99F8BDB8BAEC292B87A1486",
            ["packaging/hermes/patches/0054-preserve-lilith-native-self-continuity.patch"] =
                "E4B7D0DFF679EAA78729924AB63A75E28C3F1A5488C0AB2904E3282A33291F8A",
            ["packaging/hermes/patches/0055-require-lilith-native-proactive-replies.patch"] =
                "3538E416B3580EF83E56BB3F3592AB1DDFBA3FED2CC93395396055E9ACD74EA9",
            ["packaging/hermes/patches/0056-bind-native-continuation-speaker-direction.patch"] =
                "F8BA9410830E94B45038EF91E93B177FBDB36B091D2CEDF27204E0D387F77B51",
            ["packaging/hermes/patches/0057-generate-foreground-proactive-replies.patch"] =
                "F1E83FB2F339D696C2410A39F000C497BF99F7DCE32CA5AC75328736664A11B3",
            ["packaging/hermes/patches/0058-ground-foreground-proactive-replies.patch"] =
                "E58D5D605963A6FFF1F53E66D05BD9C4AE9DCEDEB1EAE0501158A4507A570727",
            ["packaging/hermes/patches/0059-expose-typed-lilith-notes-host-bridge.patch"] =
                "9577706ABEA0E3660E6348178A7483106DB62C5582A4EC5145577AD4F48FDF0F",
            ["packaging/hermes/patches/0060-serialize-native-messaging-hidden-session.patch"] =
                "386B12BCB4236310295992E177973BC742217CAD4EEC770D252B6448B5B067A7",
            ["packaging/hermes/patches/0061-preserve-strict-gateway-identity-context.patch"] =
                "5AC82E110C340A978BED0E5D4703DE1BC77E888722AC5A4845A496D1E33FF588",
            ["packaging/hermes/patches/0062-add-durable-profile-memory-seams.patch"] =
                "DB788B9EC8D1B713A414DC21C32A6881E84696710FB0C9572D1E5D4DAE71BA7F",
            ["packaging/hermes/patches/0063-preserve-host-session-identity-across-messaging-aliases.patch"] =
                "F1B07453DF777240FA1B426914FFE66A51C02EC5F3B15ED840E235D63BCCC662",
            ["packaging/hermes/patches/0064-enforce-single-player-profile-memory-authority.patch"] =
                "F3957EB1AAF83089B5457DC62D067EA482E03223BD36FF8ACAC26C90CAC0D0AD",
            ["packaging/hermes/patches/0065-enforce-non-recallable-profile-boundary.patch"] =
                "45FC65F980AD7E5069BB54BF66C639B1707CC98583F41A6058F1581D6CF6AD09",
            ["packaging/hermes/patches/0066-preserve-current-turn-across-role-repair.patch"] =
                "3554B26A83BF2655CD750022B1C4BF2EF542C3BC2A3F7292EDE205A1F57E7EC3",
            ["packaging/hermes/patches/0067-bound-speech-projection-reasoning.patch"] =
                "2B4FA2AA707F4DD10F6A70F90CB68DBED629D1284BC56293DC39C714C73F5AF5",
            ["packaging/hermes/patches/0068-add-isolated-noteinbox-draft.patch"] =
                "A0897D64E689960B0E736AC51EF93F8A273D8961E7DFBCB9BA8517E95977626A",
            ["packaging/hermes/patches/0069-expose-typed-image-note-host-tool.patch"] =
                "7EC5C7DEAAD5765B92440608C1DC1C3911481482D370A50A9DE5E2598A79F9C5",
            ["packaging/hermes/patches/0070-add-bounded-native-daily-note-task.patch"] =
                "F5C08E00CC0F49D8E5902917D6B960E65B94E0DA6B5C6BEFF280849C91F84377",
            ["packaging/hermes/patches/0071-d059-daily-note-readiness-and-native-approval.patch"] =
                "BD3D00D063862D356E24C0547B201FBA0BAB2C756AD5591F39EC0A3EC21E3A9E",
            ["packaging/hermes/patches/0072-d059-lilith-letters-scoped-text-and-history.patch"] =
                "36AA53DF1A079F4EA2FA5931C4BDE14782CCAD9947D74E72A1B161CECFFE83FD",
            ["packaging/hermes/patches/0073-personal-single-shot-thinking-cue.patch"] =
                "4C065747A03252DCDD0FDEFC03F86E61A3B57D639358760C1B427C1CBA4D0680",
            ["packaging/hermes/patches/0074-preserve-compaction-durable-receipts.patch"] =
                "F0E63D34DF8A7D63A52D5571C22E32971039B44CAEBB9FFE04BAFE510BBBCDED",
            ["packaging/hermes/patches/0075-native-tools-windows-managed-cli-and-install-receipts.patch"] =
                "55C774F918942E7E82C24F4D18CFA584A3845D222A9279565C6C07A19F8DD605",
            ["packaging/hermes/patches/0076-native-approval-only-resume-digest-proof.patch"] =
                "9B86C1F0788BEA37CDB1272C7BD7CF3B516A6C440FB9B2085BA91EB3E15917C0",
            ["packaging/hermes/patches/0077-native-setup-chat-bridge-and-discovery.patch"] =
                "A1D41BC721D40274AFEBC1C05D4DE75E2C108CC03DA04E369332B822B9ED7D06",
            ["packaging/hermes/patches/0078-native-tool-readiness-refresh.patch"] =
                "263FD9524A9D62ADC46EC0A1FE839F87E8575B7FBBF3F5C6D18C55514D5BD922",
            ["packaging/hermes/patches/0079-telegram-native-authority.patch"] =
                "3C277EBC08A8341702CB603A4B47573BCF0D36F9566CB8F28008777252BA23CB",
            ["packaging/hermes/patches/0080-telegram-f7-approval.patch"] =
                "E7361012684F12C3FD675C46EA28181E2802A7D103A28ED0EEAFC3399D3C5DC8",
            ["packaging/hermes/patches/0081-telegram-native-context-guard.patch"] =
                "2E0C7378C74EC1AFFEC464FA9B2443148616F61C97B692E7B8AFBE87B8EF71D2",
            ["packaging/hermes/patches/0082-native-setup-manager.patch"] =
                "8E76896965C2E3CA5E235D9267377F779886EA7D49DBDF9AADC3C462EF5E6990",
            ["packaging/hermes/patches/0083-native-mcp-catalog-manager.patch"] =
                "213CBB3117BF8BA77CA934185F34E680229F42F1C34DA26F15AC912B1FD9EA3A",
            ["packaging/hermes/patches/0084-telegram-lilith-voice-replies.patch"] =
                "A789FB9EF66A6532719A42CF32CF7FA3308BF0E5A6F3D14615623EB58B171852",
        };

    internal static IReadOnlyDictionary<string, string> PinnedHermesPatchedFiles
        { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agent/minigame_answer_repair.py"] =
                "4FAB3C0376E353F443379FE2D63FF0C2ED93529368DA5A353DCB94BC201E150F",
            ["agent/minigame_context.py"] =
                "1CD9D86728375614B7406762D386B5D898D6A9D66354593DA6B5A76A2A06A485",
            ["agent/history_checkpoint_cache.py"] =
                "8753D5A871D42514116C9AB9404B9601D4F786B71FCBE36DEA15DAE503B8F571",
            ["agent/local_compatibility_checkpoint.py"] =
                "95B66DC209582421DEA407F402DCFBC2995DF4D04B1CF43D26F72EB11FEBA0CA",
            ["agent/aux_accounting.py"] =
                "8360D98B0E327BEE26C7C98D8E95601CD48A166F350DE5ABB1956CF0CDE4CDC4",
            ["agent/local_compatibility.py"] =
                "7D4240DB5E5A75B680CE3BB4F19A5AF838EC5C165C420265AF6734E3DADDFC0C",
            ["agent/local_compatibility_capacity.py"] =
                "056EF6AF7426E3919CF58246FDFBDD5E5D3CB8172BA06A33410754FCD7BD8DE3",
            ["agent/local_compatibility_preferences.py"] =
                "053440C3E95D787DA7F6670912459E8E6906F96BC3676D2C9B20A1A8543ACF18",
            ["agent/local_compatibility_store.py"] =
                "DC60B9E60EED24206DD2B2687DC81CFEA7E059D5C64CFACED4E485A7DC9CC1A7",
            ["agent/local_compatibility_summary.py"] =
                "569524B33402BA1C9A8D5A836DD9D5240E13A72222BB727BF245086880D14CEB",
            ["tests/test_local_compatibility.py"] =
                "AC3AC0C08F0A9D61C8F87DB427B9EC5C772D9EDA74869C512DE917AEE567BB3A",
            ["tests/test_local_compatibility_host.py"] =
                "B8372811FB03F7B34175E2A54F3F30F367A81C13FAA10408D2A76EA7EE004676",
            ["tests/test_local_compatibility_preferences.py"] =
                "79AD8B5107A166D1DE83ECE2CD1C2CCC949AE2C193CA6E70F4F85CA11B86C4C4",
            ["tests/test_local_compatibility_rpc.py"] =
                "6A2A1D8B5ABEF4B387EAA3C629ABCBF27A0D86B5BBE9084C3541FFC4D7EF60E8",
            ["tui_gateway/methods_local_compatibility.py"] =
                "B652D4D0A7DBF50F25998C9B0CAC9D87F96D1364EBD399A3E0FF5BFAD6953F7E",
            ["tests/test_local_provider_session_repair.py"] =
                "6C4B12D7E08C773BB1C19CECFD26EFD426CA1964AA7EBB41AF35E881A22CF3A1",
            ["tests/tui_gateway/test_game_resume_current_default.py"] =
                "6917E9555C41F63A6159A885D5B4BC61A1A24661F37345C3E493DFF7B9FFDC46",
            ["tests/agent/test_previous_state_recovery_receipt.py"] =
                "832F2D71BBB22C98AD2CDB8D7430FDFA5A5954A8145945F16826C1821C20EF82",
            ["plugins/model-providers/alibaba-coding-plan/__init__.py"] =
                "5054401B9D5928D2328E6C50F4EA7CD74FF9CCF4FDBB0F5C6064E282B184E072",
            ["plugins/model-providers/alibaba/__init__.py"] =
                "06D1141BEB620B2B75179EA14612BEA9A24B7FFB7206A6EA9EBD6AFCAF64BDB3",
            ["plugins/model-providers/kimi-coding/__init__.py"] =
                "D84ACE10715E2261CC6F02CAA1F375AA32CCA143C088D6BD953F6524A216DC2F",
            ["plugins/model-providers/minimax/__init__.py"] =
                "C29A09367BAFB308749F9B810AACB44D02F9B4994A8B311D5562DA1901F0A49B",
            ["plugins/model-providers/zai/__init__.py"] =
                "9A1B6F9BDD0FA1C8FE9F2A58107C14158A846E4985E0409067744FF55116F391",
            ["providers/base.py"] =
                "FFF564F640417D5B8FD556F37F70D19F0EA96EE4027140FD9425031911942169",
            ["tests/test_provider_regions.py"] =
                "AF0409F76ACEBAFE4186BC31E249EF1EA273A9BC5FE3207D1399C239CC33BE5F",
            ["tests/test_scene_followup.py"] =
                "9BEDDADFA67C95FE56FE07ECA448374BA1B711B099AAD1519B62BAF57CEF9589",
            ["tests/test_scene_request_persistence.py"] =
                "D331ED3BE75BC4A455CF81BA6C21784741CF4541DFF8ED7FA2F25B9B64379456",
            ["tests/tui_gateway/test_scene_followup_gateway.py"] =
                "D9BE78458EB3A964B328E2EC48EC3526AC080763386D4A8DFB7BC0EE1F7D974B",
            ["tests/tui_gateway/test_native_model_transition_recovery.py"] =
                "0CA897F897F3B5859AC66B4F0EBB07DF28AA2D525A1AE9EA17FFFA5791D2EB47",
            ["agent/proactive_context.py"] =
                "39F440F29DDB72C910A85AEABA3C861C1B36E6562EC991901A8B585FB1593E6E",
            ["tests/test_notification_speech.py"] =
                "1824CC3E7D448AA201D72BBF3BE114587BC1E820A7970502D83002B6DE82839E",
            ["tests/tui_gateway/test_proactive_scene_context.py"] =
                "A81E92E3B22D5D21D2A98B4C2180F62C07C8B94B7EEE246A29941B0C4528AE24",
            ["tools/lilith_scene/brief.py"] =
                "F97F5AAA55882C2824BE41AA80BDD918890E7E4043E94773BECB1AF8DDCBC747",
            ["agent/image_routing.py"] =
                "5F8FBBD9470653CD976D01E9DA145D41A025D80BF40F057F8B1F59B8E0F216CE",
            ["agent/managed_custom_dispatch.py"] =
                "257994C60A27DD1E4FC0849207DCB018956C858B4890E07CAAF86CAE09A4AE34",
            ["hermes_cli/provider_setup.py"] =
                "193345AAA937715A9D3F8DFB00CFC52F792CD2AECDC35F23394C11BFB5E66481",
            ["tests/test_custom_provider_routing.py"] =
                "CFBC2575C7D534BB26E8C437BBDFD7E10ED995572479CE6542B4920BAE93A992",
            ["tests/test_provider_setup.py"] =
                "DAF379A3BD287F455197CF220251A7CD986813B8B38E6024AB65A130B1C92E7F",
            ["tui_gateway/methods_provider_setup.py"] =
                "0307EC22F7087381F4B4A4F57F7D77369EA75B68E8BBE2BD32BCF885BC62C520",
            ["tests/test_local_image_format.py"] =
                "6F53E88E5127338EE0F9D0E4E4F70745638F3A23AAB96991DAF4C80D76EE9A82",
            ["tests/test_six_level_stability.py"] =
                "E99BBF64785879DDFA1B859232CA1F2F73B9230CA80F3CDC3560B1A2C2D27BD5",
            ["gateway/local_image_preprocess.py"] =
                "7AB7E80EC894F2EAF878484BFFE9E39C24E0359DF39ABF17000F30F5E06082F9",
            ["hermes_cli/copilot_auth.py"] =
                "DAEC889E1098E13006376812B5417FB1DA24499D33215A6D2891196A5E267098",
            ["tests/test_telegram_local_images.py"] =
                "8EE1E14D1AA62BA7A54CDF79DF039597EE83453CC8A05670F9E0D67C118244DB",
            ["tools/lilith_scene/attachment_contract.py"] =
                "EB560C69DB7CB5A47645B4F4E8123C0C56767107F16A06E9D3BAAE78A86648ED",
            ["tools/lilith_scene/attachment_host.py"] =
                "79DD4F42BA17F123C0530BCA112240FB4273F68AAEA9CE2FE9FABFD0227ED040",
            ["tools/lilith_scene/attachment_worker.py"] =
                "30086DD0F3F6E4E924C8C51C1E64E688BB48F4DDBE40230773129C6AB8919C00",
            ["tests/test_six_level_strategies.py"] =
                "7D4095E4686636326F9CEB4B48EB08F1B524AAB7B9DFB57B0A42763B118A215F",
            ["agent/lilith_tool_modes.py"] =
                "73699E525625BC724FCA34BE9AE62B93960233C661A4E4332836B607F0E161DF",
            ["agent/speech_recovery.py"] =
                "8C004680DE6968259F0581E5E6FCB28C97DDF297621B51160435BB268613685F",
            ["agent/usage_latest.py"] =
                "3DE30301E3FC930BAE8558B3AE9BF6EC9643DDF1F2C05471342160B89F60282A",
            ["tests/test_latest_usage.py"] =
                "041084761E540D32015C27DC2681C3970454ACBA3456735A81E5C2102A05E43C",
            ["tests/test_speech_recovery.py"] =
                "726482557D530E77AFF506DAB57675C79428996A5E38E80B00DC0EC1A3AFC29B",
            ["tests/test_speech_recovery_integration.py"] =
                "3D06A3B4B700F3242B6A2313602D9CE0F8CDFD5E53B6DF3F00F94AE0119E1FB3",
            ["tools/tool_search.py"] =
                "28BB1C8B0FCB95EB8294AD41CE5272E4E833D35ACF82E862F5AB8CE9E7811E08",
            ["agent/native_extension_delivery.py"] =
                "BB8B84FA5F35A8A906BDB4EB6ED8CD4C59657F5AFBE3A73EE6EDBFE5B7F7E96C",
            ["tests/test_proactive_delivery.py"] =
                "82239D05E34BDFB8F9E9B2D1CD59295812702FD2C281924243C70D1780EC3130",
            ["tests/test_speech_first.py"] =
                "93859CD1CB460A7678F86D3F2C2A78E88339EE689C435EAD7FD7795CC0E1BF64",
            ["tests/test_speech_first_integration.py"] =
                "BDC6AB1D5E4F6261B2E8C0BC6A09FCABA149822A0E13F033D248B020A174A1AE",
            ["tests/test_speech_prompt_compaction.py"] =
                "03561BB292ADE853BDB95B8569AC543A69F7B6048C767D7CFA815418631F28F5",
            ["tests/test_speech_boundary_observation.py"] =
                "619C099301BEF2B4A162236486A85A3C62087D561F915028754EE227B4C63B73",
            ["tests/test_speech_output_contract.py"] =
                "AE73EB7132F3F50CAEA81C6A9FDDCA783CBB5A45DBD46BA121E614B958D9CC61",
            ["tests/test_speech_wire_observation.py"] =
                "FA622F7772BF16A8C938594720DB6B71AC3C290B364E02C352C50364BD30EA4C",
            ["tests/test_background_cache_handoff.py"] =
                "40850AFB20F949B59FF6050CD24132039EF63B37E94C6FC1256582F9A28FFF18",
            ["agent/background_review_policy.py"] =
                "DB9E515E180D16AA0FC7454242DA8582151EFA6BC3E841F56A56982CAF08F199",
            ["tests/test_background_review_repair.py"] =
                "3ECD260E8A02F2424B50CA0733D44607C1F4F9232A2872DB916D6D8AB0F541AF",
            ["agent/history_checkpoint.py"] =
                "0FBE82C6374A9D70B4CE951A6CC76177A89B73856B93C267CCD30A743D56FCCD",
            ["agent/request_cache_diagnostics.py"] =
                "4A876B4DC4CB3F1FF8BDE3B61E1F44614DB1A86724416259A17B0ED944C0D301",
            ["tests/test_context_cache.py"] =
                "0CA534D9C49DE398D65ECFB2C3BB68E25827B4FA6F037086F7CDA2C18FC3F743",
            ["tests/tools/test_local_scene_repair.py"] =
                "D2A894FBADFE8F117B0B15784EE7A59ADE0F517C2E14DAD771B8CD901B997651",
            ["tools/lilith_scene/errors.py"] =
                "6536F6ADC2A2DA3ADE135DEDEF0BD8C112ABE00897288B2AF8D2F938B0A70CC2",
            ["tests/tui_gateway/test_local_scene_opt_in.py"] =
                "10CC10B37DCAE55DCACEA98DF6785C34EF6358979CC9E4C3E4EEDCF9406181D9",
            ["tests/tools/test_local_scene.py"] =
                "9711E1EE4B52C2CE1F49BDDE7DF36AFFE3D99CE113C7C8A7B807EFEE129DA177",
            ["tests/tui_gateway/test_local_scene_extension.py"] =
                "CDF5FD18F9C39CE106ADA252F89A333636D34C31ED8A688A31C0266094AF8E1B",
            ["tools/lilith_local_scene.py"] =
                "BF3848EA263E1A90CBC3631BD164195E050397F420AEB45B371BB3B318F9C97B",
            ["tools/lilith_scene/__init__.py"] =
                "AD7D033BDE65E96966FD0B6A11D790EBF0F55BE87E271E42ED6FF2F9B421CD65",
            ["tools/lilith_scene/anchors/layout_prompts.py"] =
                "90C9A2D964814815E236F9BB07B4298E6E5936C7325F9CAFAD65AC91F977F0CB",
            ["tools/lilith_scene/anchors/layout_router.py"] =
                "646059FE1526758330E7CB5E43018AA42D87898537E399A5C8EB4CD9C6D983D1",
            ["tools/lilith_scene/anchors/ocr_evidence.py"] =
                "D1FF1A12CDE490779513DACC2A3FFB0D5D02E82264F2D05D46E728370B7F681E",
            ["tools/lilith_scene/anchors/planner.py"] =
                "AC7581C2268CB2D703327466F59FE9B552BF44B9B45752977941AFE62290BEAC",
            ["tools/lilith_scene/anchors/visual_text_policy.py"] =
                "A72693E6971C5355074633F0BE835821D5330E4F3B23214728B0E9E2408149BB",
            ["tools/lilith_scene/host.py"] =
                "8ADB3DBCBEFA5EB42586F5949335C66FC09AE67F1C5A099C2FB91C0D5335CADB",
            ["tools/lilith_scene/perception/contracts.py"] =
                "11D1066A133853EF18ADD374C6D1292C37FEF1FDD6EA1C5C7F2FF0E85C0F7D3E",
            ["tools/lilith_scene/perception/dialogue.py"] =
                "F426B3608D656EE1E7314F9F400057E65B14E4A552AD98C5F15C0CB1B1CCE1F0",
            ["tools/lilith_scene/perception/evidence.py"] =
                "790244494148C30B85CF0A8668D8DB352D59A931CEC22D9EE19C1E59F706B724",
            ["tools/lilith_scene/perception/layout.py"] =
                "C208F67011C4F04AA35B9B8357FAFCFB7FDD5860B50EE58603E6518A5B5D75C8",
            ["tools/lilith_scene/worker.py"] =
                "FB528A36FDAA769A8D567B592FC89F7BE07685D1CFD53A591BE750DFE8D13AD5",
            ["tests/tools/test_coordinate_contract.py"] =
                "12285D4E5CD521CFA3CC645235E818290E52E74625B97914212D54CE6D629B97",
            ["tests/tools/test_vision_scale_disclosure.py"] =
                "FA0FEA749A7EE268566A2FB57B79097DDCA90516E7043325131EB3415B0B5555",
            ["tests/test_general_latency.py"] =
                "0C599112E61BE5D15AD795B09360CD16F5ACE4F2DE4B4EE238E805510ADBC4EA",
            ["tests/tools/test_general_tool_latency.py"] =
                "62CE2BC509EE892ACB4D2EEEEBA3818344F4AD2240ECA63DB407718014CAE6A2",
            ["tools/windows_known_folders.py"] =
                "0148774FD9FEA767005A0120978ED965C48201F2DF8F61D0D50618E491CFD86F",
            ["agent/background_context.py"] =
                "06A5111FA8CF3444EE8542FE2C5AA9CE8A76250BFF26CC3FDA44593761ADC46B",
            ["agent/tool_result_projection.py"] =
                "163065A61928A78A2F4E5CE266A5B27DF95E9B3D009836625D4B21320D7072EB",
            ["tests/test_tool_efficiency.py"] =
                "7D86A3170807F4DEEDD596A6780B6E4D4E5B68B4D3727B9862F953847C6D70E4",
            ["tools/computer_use/windows_app_catalog.py"] =
                "3E51AE8C415E27BDE2EE80D66856C6DC93C64ACF0BBDB09D4658F89B116172B8",
            ["agent/history_context.py"] =
                "B248A2833D05B79CBA4894D0DA5B3C076D31B68B6C0BFC33EC87577E84200E7A",
            ["tests/test_chat_context.py"] =
                "402B18C132774441CA8384412DEDECD7846B565DCEA099D52D31AD428354E019",
            ["agent/paired_speech.py"] =
                "58B7D73FB4CFFAF0B04E21007F0825B524D6C82F5C975B7A78BBECCC678C4D93",
            ["tests/test_paired_speech.py"] =
                "C380C00474124EF3179396653A233D707F80FC4C2989A8F6C14A7DEAF54FD6B4",
            ["tests/test_paired_speech_integration.py"] =
                "E99A630079CF09830FD910DB18F0F43943267224B8768BAA54908B08301E2616",
            ["tests/test_completed_speech_origin.py"] =
                "5ADEE03CE5FCE7BB09B47A0F9A5AA968E26E57A26C70399192A96E69E5E667C8",
            ["tests/test_speech_origin_integration.py"] =
                "0F561692904C66624562930CCDDD48CCD8C5F11B4BE54C37194304AD436FC681",
            ["tui_gateway/speech_origin.py"] =
                "2349F629B7D5BD51D09A15F10E10B0DA8888FC4FE4BFDF266D70918FBA4FB567",
            ["agent/usage_strategy.py"] =
                "30F1CE518E92C9C67915B53E9F8059B55C800D9B15209D028434BA10CFC58C73",
            ["agent/usage_strategy_context.py"] =
                "F927FE42F5A5C1D158074577D69F055F0ED8430C1D226BF5244DAA27468F6755",
            ["tests/test_information_strategy.py"] =
                "1C3CED1B92946F874B2357FF1ED738B3EC8DDC55C1511E8B9C4C10BBEB962274",
            ["agent/usage_budget_preview.py"] =
                "D2860C68F010773FEA568A983F07C584D90B9E938C7DBC87983EE5D6F690F6D7",
            ["tests/test_lilith_usage_budget_preview.py"] =
                "907AF22ADD5750C81E6BF8028534A471307E02E373CB156E0A5D54F3A8221A73",
            ["agent/iteration_budget.py"] =
                "AC811026C0C28BDF26356AC976E074B2AB1673E6032E3E0A3D0A5407F04385F7",
            ["agent/usage_budget.py"] =
                "C9FDA81A47563703B6B2AD5FAFCD4C17283F4FFFBCC3095DC75E2510B1AA6A47",
            ["agent/usage_budget_compression.py"] =
                "4B71D4A0AB2D226A786F1BD3405EE045C8B7B1EA191686C612B3AD7DA107324B",
            ["agent/usage_budget_context.py"] =
                "7669AC94F037B202147624181516B83F90CE52E903DD16792E938C615CB14437",
            ["agent/usage_budget_dispatch.py"] =
                "3C885951EDC1BF24D37D2476DC4F7840B29351739F5CE32C404138B9DE2C4B7F",
            ["agent/usage_budget_entrypoints.py"] =
                "0B8795F1A34A2D090D9C098C9E21EF6A7BC160A162F8552299C8C494732E5CFC",
            ["agent/usage_budget_host_policy.py"] =
                "B8A4245D6E3D19A86F9700AA32C03EB0079F6400432C52FD5D8F333281FC3032",
            ["agent/usage_budget_native_dispatch.py"] =
                "78D442520C6ED3AAD4A55C55897CCBCA80885C346EED58DD6A4F28CF6ADFA5A8",
            ["agent/usage_budget_official_routes.py"] =
                "40F9B995C4BB89E10FC7D295787C428F192664D860939EE65F3A2AFDE5AE8ECD",
            ["agent/usage_budget_preferences.py"] =
                "6A66D063948E6BFC2CBC42B555048F42682F3BF3D07F34F5EA94CEA7ADF1EA46",
            ["agent/usage_budget_projection.py"] =
                "FA72590BE593A2A377D5DDA43B4F926860DD416F8E60C52DA340EFC753464CE4",
            ["agent/usage_budget_runtime.py"] =
                "F0DFC4D57FED088D80A0A9C3FB4E248BB86854F65A8ECD4809A36E3CE7A9084E",
            ["agent/usage_budget_store.py"] =
                "65C0DA929DB8C3A086C4ED2045AD46E5173D86153F78FFCBDCD844485B617146",
            ["agent/usage_budget_usage.py"] =
                "3BA84D1675ECCE870299C88C7E38BF1338220F0F3DE00CA68F68AB06A9CE9E87",
            ["tests/test_lilith_usage_budget.py"] =
                "9D710AAB9D20D8045AA0A398896125F94E331F442597D05DE32F985051276690",
            ["tests/test_lilith_usage_budget_compression.py"] =
                "721F726AB149313F665CB5905DB8E114073299788497611AF150BD0B27D650CF",
            ["tests/test_lilith_usage_budget_context.py"] =
                "0F7A1175AF58E895130722386EE71E1CDF27DA88CD42DB089F0566637DEA49CF",
            ["tests/test_lilith_usage_budget_dispatch.py"] =
                "A3017776AF6E4ED0EC6BB557C1DDD4340A29277CAA2B3DC051ED5ADCEC015C6F",
            ["tests/test_lilith_usage_budget_entrypoints.py"] =
                "F905216EBC96DBFCA2BC1E4F71DBE477C10B4F6F16DC411F2FEF288B8C6B80C6",
            ["tests/test_lilith_usage_budget_handlers.py"] =
                "958109C615FE09912EA13F3088824B9C8A4147F1792F098E804B2688808495E8",
            ["tests/test_lilith_usage_budget_host_policy.py"] =
                "B23FD206F1E3994032705761D870C9BB2290006E4C3EA5AEE2762570A6625F9C",
            ["tests/test_lilith_usage_budget_native_dispatch.py"] =
                "3BB5F5FA928EA09A6A10D1F8C625A0D60EBD126BBA9E1ECC1A0CD69D5C859604",
            ["tests/test_lilith_usage_budget_preferences.py"] =
                "0805A57E74B534CEF40F95F936A36E3BA312C5A62EB0E2C84788E17392A00C38",
            ["tests/test_lilith_usage_budget_product_flow.py"] =
                "7A43F204F8BCEC7FC658D788F4E709644CE6A333C5D69693EBF7F5916F748837",
            ["tests/test_lilith_usage_budget_projection.py"] =
                "223D73F545C81AFE573C8A72330058111A4F1A0DEE132761E4353C91C103B303",
            ["tests/test_lilith_usage_budget_provider_routes.py"] =
                "F073DB4BBD88F57F872BC9DB4B08469F943861556F484463A10394BDE8A8DCF4",
            ["tests/test_lilith_usage_budget_runtime.py"] =
                "DE92ECAF6D81251C26554C96C1D2BA8A669197409E1739F67DD8AB910E6B79A7",
            ["tests/test_lilith_usage_budget_scenarios.py"] =
                "B7EE6F8F31C87163F768D68ED56705D7CA9B62278E5C670B4AC615ED76BAAB54",
            ["tests/test_lilith_usage_budget_sdk.py"] =
                "51B911A5DE63A7736191FB3058E5E7DA5FBAA8CA5F5D7D8774097AD9ACE2B6E0",
            ["tests/test_lilith_usage_budget_store.py"] =
                "3B91188F7391D9F9302A24E2B658392DDEFC6FF61892208CFED173527CA23297",
            ["tests/test_lilith_usage_budget_tool_ownership.py"] =
                "11900212197E577A5381B48D1FF282A8625D75F9DF003328D63C17221B103C3A",
            ["tests/test_lilith_usage_budget_usage.py"] =
                "2C26D518F5752C809F17CBE0B61FF7CD01C117D0F195B76FF61B1195924347AA",
            ["tui_gateway/methods_usage_budget.py"] =
                "2DB5199454C2B1F913451017C4F9E7E3E26F3164920F9547491A4BBCECBCCED3",
            ["plugins/image_gen/google/__init__.py"] =
                "3DEAE37BA0D34F63A00866E13F6E0536C713940C2737097E9A56B8B452A17AB1",
            ["plugins/image_gen/google/plugin.yaml"] =
                "89846C265D13789EE9613A92D03568BC45AF8642B4F2160BB9D2064F6B026B12",
            ["tests/tools/test_google_native_image.py"] =
                "F95FA17AD0B7008B5F73E032EF320303A4D52CA3EFA3DD50934F6768CED2CE9A",
            ["tools/image_source.py"] =
                "2DAC0D8E1FAB691A65B543F5BE918332A463E5295468EE71115209B46B8C4EB9",
            ["hermes_cli/curses_ui.py"] =
                "9DA262577908B1FB028140804BF831426A941A5B6FFC0A8703C71A3AE4F2511D",
            ["plugins/image_gen/fal/__init__.py"] =
                "67980F1EDD7628FCD1C567F989B5E88253810DB261B7594B04ECA058F0415D98",
            ["skills/productivity/docx/SKILL.md"] =
                "ABD2A6657B9AACB1C52549B39EB224B17D8148F0349A4FF9FC70F2782C09F044",
            ["skills/productivity/pdf/SKILL.md"] =
                "1560FA7367692DB9F2A7050FBC64F3770374D4C61145D0340B4EA175D2D7FF8F",
            ["skills/productivity/powerpoint/SKILL.md"] =
                "431077E4DA88598751F56FB80AED924278A45E9EAC275313098477207E21C2F1",
            ["skills/productivity/xlsx/SKILL.md"] =
                "EE09189B7F658344FEFC50297BCA7111FE14C0A6DAAB863799538EAF37B4C28A",
            ["tools/browser_tool.py"] =
                "A5721D727320F561BB4008DD8ECA55889BDB5EBE54256DFCDCE28B7D9EEB2D34",
            ["tools/environments/base.py"] =
                "109C9CE889A6EF2F18476B0446F98726984688B1446887E184629F1B48840FF4",
            ["tools/environments/windows_job.py"] =
                "C77FAD79A31FA2517B4A22E9A76C469845348F0979FED407BC48A2C9BAB1F3CC",
            ["tools/fal_common.py"] =
                "3642B570C10C27051561044EFE9F573F5635168A030CD3E84C6D170E41E239E0",
            ["tools/image_generation_tool.py"] =
                "52D59E85A6944402672AA908E954121589071B32D1EEB802E7BA44E0E63139F6",
            ["tools/managed_python_dependencies.py"] =
                "D4ACB3305A37E67F3CC88FC5E6244C662310E14E5C3F7278DBE56EAC800EE670",
            ["tools/managed_tool_dependencies.py"] =
                "EFF0F194C2F1BF255E9FC36F00EA51F93BCCCA3EE502CEFC95504238A8A3B96C",
            ["tools/native_setup_locale.py"] =
                "A575C3138754B5A112FB96F01FA42F403D7D78E833DC5C755AFCD4BFD85C3ECA",
            ["tools/skill_dependencies.py"] =
                "A176049E2FAA8A6A868ADC564DCECEE6E6A85B75399B980E7E81E353A42B4C10",
            ["hermes_cli/model_normalize.py"] =
                "242525E66DAB7CDFD5C42213A5730CDF99D1B4A1453B452A14628598EA7EF713",
            ["plugins/model-providers/deepseek/__init__.py"] =
                "4D385CA3831AA721DF540FD1544BFDED5C3441364EDC5E44B93EFFAFA65CDA97",
            ["tests/hermes_cli/test_model_normalize.py"] =
                "0A9833FDBBD5B997D0F7B31FCE0B9F147FDA1694E7C7FCD515ECF16C9D47AD32",
            ["tests/plugins/model_providers/test_deepseek_profile.py"] =
                "D9E7A3E21325291303C438E204079E1AA13B2F693749AE547C493A05B1F3E5EC",
            ["tests/tui_gateway/test_deepseek_exact_model_catalog.py"] =
                "E1CAED81182E788AE789CDC0FC9ECDD7113DF2E3BCBC32F78DC7333F6965AB9B",
            ["tests/gateway/test_managed_sethome.py"] =
                "207FFE81B1FAB327BAB5B059343773EA203DCCE6CB37421385D105DF00B90B6D",
            ["tests/tui_gateway/test_resume_durable_replay.py"] =
                "4E31B1B6B98A00719FFFE6BE47B143F7CCA430A5D57BC8147D49D2514BB0B6AB",
            ["tests/tui_gateway/test_session_resume_db_ownership.py"] =
                "B44916A50186A73C0E48EB587C4470DE47B923DE3528CD0FEA99A4685FBCB7F6",
            ["tests/tui_gateway/test_native_extension_durable_replay.py"] =
                "84ED89D8B9BF6D0A5D7CA6B3B84CE597B17BB75D5A4B1162280B696800670370",
            ["tests/tui_gateway/test_exact_global_model_selection.py"] =
                "22103AEF6C16A9827C56B8B84976D20151B0036F88207DED440415CA0784DDF1",
            ["tests/agent/test_speech_examples_expression_context.py"] =
                "519BCBE9DC68A0D8BBB0447C3C8FF46AA07C8F3E3025F5CF4B2758B93695BF7D",
            ["tests/agent/test_original_experiences_expression_context.py"] =
                "EE1B2656A1C8D6294CCD66DE4EBF0F7A66DB10E37D6528529D69EBE498D32D25",
            ["tests/agent/test_tulpa_expression_context.py"] =
                "B12624E6FB35035B1EFCC49FE48CB91EFE21BF71CF8EA99E55A35C0C5B27F490",
            ["tests/agent/test_expression_context.py"] =
                "BC426998B0634FAE0EFB3FCFAEE9AE2B034D7DBA2A3980300AEB32F2FCBA3059",
            ["agent/player_name_context.py"] =
                "0C85E12D513AEF94C83104882E7C5556AC972B3CA25D01FD85E5116BA9FD02B1",
            ["tests/agent/test_player_name_context.py"] =
                "2B49A030570D97069292E5C855AB2868592415DE9482F34A28DB6926D7F6B337",
            ["agent/expression_context.py"] =
                "7A19A715F11714F8A843C18DA51EA0C3F143397055F5209286B7302D9CB6FAB6",
            ["tui_gateway/native_media_setup.py"] =
                "15BA19B21F915991ED01C1458DA6DBBAD446D25973B48A7BD48F8074E1ED55A2",
            ["tests/tui_gateway/test_native_media_setup.py"] =
                "61A3BB18B5DA55A4E509C67B9679D98B563F2B4608422D294BBB9CBA5D7FC314",
            ["tests/tools/test_lilith_ui_sound.py"] =
                "4A2CE7431B5D574B28ACC56BAFA9F531FE373A08058D788ABFD2BC4958565FB9",
            ["tools/tts_text_normalize.py"] =
                "F606D1B8D10FDE7A92EDA14F588540B1ECC58A450A8B508A987EDFC06E8DE753",
            ["tests/gateway/test_lilith_bounded_speech.py"] =
                "85309085290BAAD268D45918E8E16442C36B716E51EF1796CDF5C9E3A9F3DCF5",
            ["agent/lilith_speech_segments.py"] =
                "2483C48E00D5B433CB6CE80CCD8A86272AB618F1212583CF4FE8E8705CD58609",
            ["tests/gateway/test_lilith_speech_language_validation.py"] =
                "D2D8C637B8925E0D02C99B21DDDEDF4C3BEDFB0E7682FDC72C1ADD20034977D0",
            ["tests/gateway/test_lilith_voice_profile.py"] =
                "F1007B201BF34B8097FD9575B3D5FE8ACE47AAFA6BBA782466E301E0AEE07C04",
            ["gateway/lilith_telegram_speech.py"] =
                "58D5B695461D51BF3513E56A71E9C0DFE4880622673FF6E74134EFA6BDD25F79",
            ["agent/speech_projection.py"] =
                "D35075CC5E9FB36DC25A5BEABA74ADE349242BC8C6C4D9B2A2F7A9C09C2393A6",
            ["agent/lilith_telegram_voice_profile.py"] =
                "5F1025A7984B1B39E1E04F820AA642C3EF20865D53B3135588B33C07DC5B9CD6",
            ["tui_gateway/telegram_voice.py"] =
                "72B1D080ED5FEE3D5A676BAB999780135AB5EFFFFAFEF5C10BF325CED131260B",
            ["tests/gateway/test_lilith_telegram_voice.py"] =
                "77BDEE9C23573F69E55C14E167B2CCE04FDE494BA7273240E087E78E289C62EA",
            ["plugins/tts/lilith_gpt_sovits/plugin.yaml"] =
                "E44E8F69B5C9E5803D48C0A126F84CCA250B3DC95AD42337145DF098758A8E50",
            ["plugins/tts/lilith_gpt_sovits/__init__.py"] =
                "2EB1AF6F176729C6CC9A15455692F40DF7F8DC2194B6B81F4578C1A3C1AD9D95",
            ["gateway/platforms/base.py"] =
                "F206EA1B4E83ED373AFA8A7BFB8F2BD2552D0B7EC938723C14A7B475E446A048",
            ["gateway/lilith_voice_modes.py"] =
                "3D0DE49B9109EC1311B1771F37C2B4CB0BB2E748000B4813D59EADD01C0E0349",
            ["gateway/lilith_telegram_voice.py"] =
                "FEF6E9247AFC499A4B4233B2CABD3D77F0278845C38681AE11EA091944BA9B45",
            ["agent/tts_registry.py"] =
                "A5809CF02E04CC953C781079C07781B5DCD7E65030B97D4E273C9AF68E0199D0",
            ["agent/lilith_telegram_tts.py"] =
                "755DCF284FE91D23ECBFBB9F7993DBDF5A1B64B854E1FF0A231954CED9AF14C4",
            ["tests/tui_gateway/test_native_mcp_setup.py"] =
                "0A8A4F82A4023A792AC1BE2FFD00E12493B2671F55EF5B1AA896DCB9D0C26C89",
            ["tui_gateway/native_mcp_setup.py"] =
                "106DFE0BC3F0AEB74B28756D09705BFFA1AD60497EDE6BED80C78AD8037EC827",
            ["tests/tui_gateway/test_native_setup_selection.py"] =
                "D42E269F8212B4CF3E94C9586AFC41C8A1F277AF3D76A49796E75851A07E4EFE",
            ["tui_gateway/native_setup.py"] =
                "0ACB69FD5863AA20F6FD741835CFC14BB2B37EB7250AB624717C021E7D7E895D",
            ["tests/gateway/test_lilith_native_context_guard.py"] =
                "8468A5491678C2FBB11FC7813A0373C447941DF72423419A9AED1265A8868C7B",
            ["tests/gateway/test_lilith_f7_approval.py"] =
                "DBCAD55DBE01DAF7D20B3280D56785F300AA3980128023243853F73D73AAAE55",
            ["tests/tools/test_approval_interrupt.py"] =
                "0BE65BC886D20F9066003103F5F94D20BDB271172E372DB22591D8C85C19BDDB",
            ["plugins/platforms/telegram/adapter.py"] =
                "A148447612A7BF3DF41C2F33345E0CB178065D420A61DECC129FCEF0FB625A01",
            ["gateway/lilith_session_policy.py"] =
                "D55B54F5304A5805708AC1EE1E5639F0E933DDB33369BB7B3EFE27607CE1F426",
            ["agent/onboarding.py"] =
                "34A7E93AE8312ACA038771EEBEB6B5437C03E248A58EEAE0295D84C54D6E08E9",
            ["agent/secret_scope.py"] =
                "EF240EBEDFB3F1DC057CA6F3E5C884BDDF8FECB952AD6BB0A8D3369667936F38",
            ["tools/mcp_tool.py"] =
                "650E928F9CC083C35ACFF7B8FAEA647BFA5BC53F30B309DC9773BC56BA374FFA",
            ["tools/native_tool_setup_tool.py"] =
                "B87FCD6CF62FB5083416CC068AE837E6E811CC0069DD6C6208146CFFA771DB58",
            ["tools/skills_hub.py"] =
                "E7A6B88344434CAD0F144EBFD601AE01159B8DB879CDCD72752DDBADAB85996B",
            ["hermes_cli/skills_hub.py"] =
                "DBF8BAD1943A46641C37A507D8964769C939DD1F5D23538FE961E9262230DC73",
            ["hermes_cli/plugins.py"] =
                "2B8F80AE6A20DA866036CC802155E4D67F4220CE92D162EBED8485CBF5B63F89",
            ["hermes_cli/plugins_cmd.py"] =
                "73414E3EACFC4A5B522AC3336FA7F477CB5CC9363722C7E82B7B1FC9FBDC720C",
            ["tests/agent/test_compaction_durable_receipts.py"] =
                "90F630ADF94313F91F67025C7F78ED5AC43920A977543FA45B419B7146005AEE",
            ["acp_adapter/tools.py"] =
                "78A48467B391B95074D9CC82710EBDD8607E2C683BF5A40EAC05B9EB6B60F7D1",
            ["agent/agent_init.py"] =
                "714B24134F6BC7506948EEAC6783AEC473B80119104521764015B53F6861FD9C",
            ["agent/agent_runtime_helpers.py"] =
                "7235EAFA1549DA2411D62F6AA7025A24CBD681F59EAEBB9B6C404B853A505E87",
            ["agent/anthropic_adapter.py"] =
                "712E7D2B8820654727DD2ACE38AAD53B675AB418C699F174012D6A9F74AC7B70",
            ["agent/auxiliary_client.py"] =
                "A36F5583177FFEEBE49E24168CE6C19D2716D58AB04693601E28AD2888A6B5C5",
            ["agent/background_review.py"] =
                "600309DE9BA4EF304F4DB0A2977603DAEA6BF61D29BB0293E388ED509BD54B88",
            ["agent/bedrock_adapter.py"] =
                "AE479D4E6A727D7C6E6D9E6C3BEAC045BCEE7826D74515E947FBFB587FB89ABF",
            ["agent/chat_completion_helpers.py"] =
                "D2EF4FDA4D4D055D4526A49DB095D7DDC52FD8A262F856B37092A7FE661B28D9",
            ["agent/codex_responses_adapter.py"] =
                "32ADFB99458C17B81E70F57511C8B397CCF6490463D4A7417CF8FA2DBE0262E0",
            ["agent/codex_runtime.py"] =
                "A7D73F00B4EBF46C8C5E68D53611073783DC34EA8B1A46696EF596EA00EF4A9C",
            ["agent/coding_context.py"] =
                "4B76CBDFFC84D7ADDFBE9F6B933B7D46CC00E78E2D07E9357E7DA664EC52C97F",
            ["agent/context_compressor.py"] =
                "42E13884BF0FCEB8D23AE20EDACF0B6D23FA503B9B03824C6BFE4C36E9E0F030",
            ["agent/conversation_compression.py"] =
                "D734AA46B122083D6292EBED9C794B72234225989674111149D938D807B03D26",
            ["agent/conversation_loop.py"] =
                "F01D0983CFCA71CA8771C1D81D180D9DD68659D907E000CA70A4469B78D2FC74",
            ["agent/credential_pool.py"] =
                "2223DFD43023817C9AE3E9E08049BFD5F7085E6D4A13C996414C11D82BE6746F",
            ["agent/display.py"] =
                "CC9B35E8D558E335ECA5E5F9C84DF0239C706C52244CB4917EEBF0E8A1174ECD",
            ["agent/gemini_native_adapter.py"] =
                "81BEB489C0E87D28B197AD4645CD72A0EEEEA53967BD38B3A62402940325241A",
            ["agent/identity_policy.py"] =
                "7FC5E93EA09FCBEC75789582427083129D345868AF91B4F8F15ACACA84E73EB5",
            ["agent/memory_manager.py"] =
                "BE4E2B3CC8ECB960549E01F0225EC34F458F7BE6366D76DED80D8A5094A1088E",
            ["agent/memory_provider.py"] =
                "6D25B547474D1BB928D8576B8B86438B52F6667E6985FD7910896F9745C23E46",
            ["agent/moa_loop.py"] =
                "753A14407852383C2269892A0E33FB226684978DD9A581C8F9AD82BB59F0524F",
            ["agent/oneshot.py"] =
                "304B3AD57E9A628FA36DDA719C0906B96C945C576792A515A23386307B59E37C",
            ["agent/plugin_llm.py"] =
                "0F9A1C3483635537BD937C9393CB14DD590079C8FA2BE12CEFA50AA0CAEF8B16",
            ["agent/prompt_builder.py"] =
                "240BAB054A1E033E16828F8B5CF5F4B53B23014DA679DE949B3B5065562266B7",
            ["agent/proxy_sources/iron_proxy.py"] =
                "081E4DDC27FD5C2C851D30A8B9560FC43867BFA8FBD817E5B3F069921CB7727B",
            ["agent/relay_llm.py"] =
                "AB249E7C81469401C5128F285FECC89C77D1B2A0D5F11C8DAA16BA5323474B98",
            ["agent/system_prompt.py"] =
                "3517F88BD07EC93A3D888F29C52F9F1BF35DA9AE44173C47A8404BBB45432715",
            ["agent/title_generator.py"] =
                "BC7921F15FF2C2859CF232C627832D1313581320567B37E02ED9D50565F03BC7",
            ["agent/tool_executor.py"] =
                "DDFBF0190C5A6860EF83B0C12D8D57F22C57D44F50DFD6586A7BEC95F291F58A",
            ["agent/transports/codex.py"] =
                "074408D3BE73D9B3A3E094F5F7B8490E022AC5767772BD81CC0DB8848549FFFD",
            ["agent/turn_context.py"] =
                "FFB3646D351583A260C5CA0F2C7DC7C34CB415F12F5014FA52F7550E8797FFBA",
            ["agent/turn_finalizer.py"] =
                "1786BA9A3AC7077994B9FAEF247DD2B591D5A9F4ABEA396A01D77AA42D808FA3",
            ["agent/turn_identity.py"] =
                "7E81B913A530E88DD42FA483272E48FF9C3CB2FDBF628F238398716CCFF02C38",
            ["agent/turn_summary.py"] =
                "11FE9FAE73FD4FF058F3A68D5A36320D141B21467143B0DA80CCF249B77C2590",
            ["cli.py"] =
                "AD92474B290F235B22EE49CE96CFAC10EECCA806B0BFF16F82E86FA862FB80F9",
            ["cron/jobs.py"] =
                "DD949F5ADE315CD2F676E20299E8AB546077D2FAC926909D33FF5A6CC5565EA6",
            ["cron/scheduler.py"] =
                "977AFACE19E1F5F389DFC39585D0D3B589330A05CF13BFAB5D919FA3971BAFE4",
            ["gateway/config.py"] =
                "B5EFA5A215EA530898E0F9A6128658ADBDE8718154F5BB8A56C3A3628886C85C",
            ["gateway/platforms/bluebubbles.py"] =
                "5A4F8272B02D6FBBB45D2F9FA09B34FEDB7FC0ECDC823C36E1E60AC3B9337696",
            ["gateway/platforms/whatsapp_common.py"] =
                "041BC2E3E82D40C4E8951F7549AF6A5EB4E5F2E600730EE512460AEBC87141C2",
            ["gateway/relay/__init__.py"] =
                "423B0A8D7E3DCA238E2E76264CFEF4CF40E60590296F3C2516BE9205D12A1A60",
            ["gateway/relay/command_manifest.py"] =
                "7C1CECFFD70A2ACBD093660DC2A4E6042B9E9A478A3822ADBC713303E22FEB2A",
            ["gateway/run.py"] =
                "7C7DF759F958EE06B8D71E5C513BA9325B0F778DE2E3171707A09C0192E86381",
            ["gateway/session.py"] =
                "FAFB2C9419F89679D5A25BEA0D74D21EAF163D5D9E6C00208D6F6716BE370F04",
            ["gateway/slash_commands.py"] =
                "16366EDF9390B89172AE067D562DAAB8817CE4A37BC44B5D1E73032A25C7E65A",
            ["gateway/turn_context.py"] =
                "C60946057F80CC09CB169FBFBBFBB15438DA33FCD46169CCC860143A8DAB71BE",
            ["gateway/turn_lease.py"] =
                "F45F4C52A0F8DC424110763BBAC02E929E82F271B5BE15A0D8125214077D07ED",
            ["hermes_cli/auth.py"] =
                "035FD155FF13E9D04420B6A90E6E0E4617BCEE976CB9C5449B55BEC91782A39E",
            ["hermes_cli/cli_agent_setup_mixin.py"] =
                "47EBB3917E95AB8B0A9DEC166BFFF6454EC84947DA20874BE640583C9E4F2E9E",
            ["hermes_cli/cli_commands_mixin.py"] =
                "AFC771A8BD47657CBB928FBCD84AF83667381BB78CF31E629F087804FE81D335",
            ["hermes_cli/commands.py"] =
                "62BFA557E2153073F7D63BB9151EF8B0AACBEADAA18055D10C97F1D2493E6D82",
            ["hermes_cli/config_defaults.py"] =
                "73EE9B13A554CAB7FFA228BE7B0D396E03068EA9C5EF5C9BAE67F055B55BB553",
            ["hermes_cli/config.py"] =
                "CC5443DE7B0DB504D62DF8C778DE840B1B31DA88BE81D01277A87CFE256AEC70",
            ["hermes_cli/credential_lifecycle.py"] =
                "DDDFF84CCA8E30B48169BE0E01752AE7F6303CBF8CC11F2D5E4991FD7791FA70",
            ["hermes_cli/doctor.py"] =
                "D54AE4BC03A1574462387C362816DAAB0DC0C818BE937D868A227A5061DA1B87",
            ["hermes_cli/env_loader.py"] =
                "85166D592A94CA5B4074BFD8C3F14F35AB0E29A00BA61103C83F9D06760AEA35",
            ["hermes_cli/inventory.py"] =
                "F6A5B12B0020982A272F288BA08AD747BB9B372D4C0E8E3EF3F91C2527AE6F5E",
            ["hermes_cli/main.py"] =
                "20DFF2C5D859A249ACA326F5449270827AB5D634ACD7DFD0DC93DC71A119D7F0",
            ["hermes_cli/managed_scope.py"] =
                "C94C138E9F7B4488431B8B9189B2DDCD5AD05D2DFA87BE90B3A17232F4E7376B",
            ["hermes_cli/middleware.py"] =
                "8A0ED5F21415462FDE74483C525039159F032E53B04EB4D4DCCC13F269C80AE3",
            ["hermes_cli/model_switch.py"] =
                "78BFCAB036C1249401A955ACEF7F33F1234E8A5221D6D1EF8838EFA1EFBBF111",
            ["hermes_cli/models.py"] =
                "28D6D653AB5A8D1755DC0F46BB7177D13420453E49DCFAB9188DC293D5F0DAC4",
            ["hermes_cli/personality.py"] =
                "9BE2284E3FBF71E8FB8DD539028C911C834B6FF2B7B2F994CD49C0110C201068",
            ["hermes_cli/provider_catalog.py"] =
                "DC247338F218D33AE9FEB36C720535E8C0C1A85802A5210E8DD869D52FCF7BAE",
            ["hermes_cli/runtime_provider.py"] =
                "E74E684B807ED7AB76CDB633ACAFD9FD9AC4B307C2ECE269061E91623859C081",
            ["hermes_cli/send_cmd.py"] =
                "DA6AB85B907B7416B71E9FF38860C6C1DC669AA0A13B1529C28A91E1FD87BBA3",
            ["hermes_cli/skin_engine.py"] =
                "13DDC3412BC836F30F0AA3C9FA2928EE29F85C783C5365094B3F63E22D9DCC6A",
            ["hermes_cli/slack_cli.py"] =
                "9BD5886C2BAADFCE761DB71A7BC32C5ACC0E2F87DC578620B83D8B2CD4D4B548",
            ["hermes_cli/telegram_managed_bot.py"] =
                "41BF8637C4B3A1EAF63103D21AF28F3DA7C7DECBCDD3DE4CD04A3FA42A6E2290",
            ["hermes_cli/tools_config.py"] =
                "0EBE80FB819F6A5E22AEE6BF7DD59E992DA07C83B96C3460D4AD832735771111",
            ["hermes_cli/voice.py"] =
                "7D3CBBED8D362BA5A76974C0AD3E4B1EC09F4C1FC85D52D550D3A6B03163B065",
            ["hermes_cli/web_server.py"] =
                "80CAA64FBE50F8EC3ED8DADDE9FA993B080D46E6AAED0F1366516E47F263C805",
            ["hermes_logging.py"] =
                "25B84D0F307B498A603E7B705D08F8A70C9AEBD92EBB6E6697AE283FAA587D19",
            ["hermes_state_common.py"] =
                "DD9B36C2D7B795AACE66E87FEAE9A6DAA31564D6E27C2F2356A1148C0BC88786",
            ["hermes_state_schema.py"] =
                "E0479981D9FEB84AF1ACFB51BF147C51C394E7CED3AAA6A0FE675DB4E5938DED",
            ["hermes_state.py"] =
                "05EFFD4BCC58876A3A950D7446470506E85FB53F32C4279C9CC3B4F7FFBF3E34",
            ["hermes_time.py"] =
                "879B0A64224D2288F4570BFBEF76AB1001716D8B82BC6E9A8E9FFC78935B4F72",
            ["model_tools.py"] =
                "12218618B50A1F4C0B1F215E8D522F512737F8075D256B86D39309C0EDC786D9",
            ["plugins/google_meet/cli.py"] =
                "55F741EF6A897F663C3670AD65EC6BA486C8D8747F730146811D2D6EBC9F581F",
            ["plugins/google_meet/meet_bot.py"] =
                "078D6317D5B94B4EB0BF23D676B99B6CFCCE8ADEBE5EC15FC9756D0463B02F7E",
            ["plugins/google_meet/node/client.py"] =
                "C6742A48DED5D767275B0E24376DC51976C4C180CA8232BAF2BA6E87A2A706B7",
            ["plugins/google_meet/process_manager.py"] =
                "55C31715E56CCCB7A99E279157299497933867D824DB31D41B188D17A4A2F189",
            ["plugins/google_meet/realtime/openai_client.py"] =
                "7E4DC4449E4DEF5110932C889445492443AB1BFE8DAB588971ECEEE9D11F5D91",
            ["plugins/google_meet/tools.py"] =
                "07BEB8E1EBA82ADEFEAC0FF3345D4F49AF5D7B10F2E6F2B64C4CFA61CCFB31C8",
            ["plugins/image_gen/deepinfra/__init__.py"] =
                "2C5D89C252F75BCAC61C7975267D1B69C14911F5722BC98D06D35B25135F9B4D",
            ["plugins/image_gen/openai/__init__.py"] =
                "AE25F1E47D40D846426074C838A304C92876ED1C8C9DEAF5BC1A38EC79B61DA5",
            ["plugins/lilith_messaging_bridge/__init__.py"] =
                "6BEDBF0352E569B4A95662C3E4C378FA8BB637D06118A11DDB50F14787523990",
            ["plugins/lilith_messaging_bridge/plugin.yaml"] =
                "9BB720C290DDD7046F973127800C0E53B51312494571825B2FD5C7D918302998",
            ["plugins/memory/__init__.py"] =
                "8F2ECE9459ECA52B74F8CA6B23D9DA12DD2631E6646BD084D3EC460A0CB67DE5",
            ["plugins/platforms/a2a/adapter.py"] =
                "914D2BB868FD68C865A18D5F895EC9AB9E0BB6C4C706C5AB9012E213736F122D",
            ["plugins/platforms/a2a/protocol.py"] =
                "5E7CC27867A8B7706005922FBB7F945B14198B07DC228951087F3BFA9FD05734",
            ["plugins/platforms/dingtalk/adapter.py"] =
                "0053BFFFF677E0B7A6561A8B1ED014D19B431EEF7FF0ADD152C535A16E94E081",
            ["plugins/platforms/discord/adapter.py"] =
                "7DF0C87CD60FEB50B008D5824A9CF50B1D360EE79EC0F95F9858EEEA2E1EF21F",
            ["plugins/platforms/email/adapter.py"] =
                "9739288D9AAF835C8DF11D6941AFBD06846AFB83EE7EA80DF0161AF4A0B1C98E",
            ["plugins/platforms/google_chat/adapter.py"] =
                "4B06B17FB358690429068DE76BB43F61E417CCC70EA02FBE03D46AE44D74F8E9",
            ["plugins/platforms/homeassistant/adapter.py"] =
                "F70EF474F62DDC49E1EEFC78FEDAF386AD5C9E3F0408614C62309C45D27E9659",
            ["plugins/platforms/irc/adapter.py"] =
                "B70F87998C9FA79575F8E4EA5CCC33CC844DED307ED1C8C751346555BD683EEE",
            ["plugins/platforms/matrix/adapter.py"] =
                "BB6CE76EB39D57D14661CB4E934D3687F58424FFFECA091B05D31AFE72DD750F",
            ["plugins/platforms/photon/adapter.py"] =
                "2C500A25792E916FE63DFAACF9B48959250CF5E519959D7C75BCACD0447699E4",
            ["plugins/platforms/photon/auth.py"] =
                "A07A8A4B102C094BE2999F86A65E371386AE1CF6891A89128FF34B0C1FB8B38E",
            ["plugins/platforms/photon/cli.py"] =
                "D1EB2C69668A627ED8B235F4D0E2D19DB16B3421F765C53C66E06332A17BD3C2",
            ["plugins/platforms/slack/adapter.py"] =
                "1AE2BEA72C71583B7733CE3D7FA9DF71DC03A12D268F0A92C570F8E70FA4A8BD",
            ["plugins/platforms/teams/adapter.py"] =
                "0BCF42703B9C1D772EAE3A4FD458DEA8DE64CA38F6029666322C046118603F0B",
            ["plugins/video_gen/xai/__init__.py"] =
                "3808C2EF5F515BB81270250174A2D6E86F83B4124670C912696BDED8B31CD945",
            ["run_agent.py"] =
                "355727593282D97BCE021915170F5F92F2164308BBD64F1CBDD80CC95885E597",
            ["scripts/whatsapp-bridge/bridge.js"] =
                "20D411BE77905214DA60EBE56AB462335C308CEACF30EC0487586A47CE059EAC",
            ["skills/autonomous-ai-agents/computer-use/SKILL.md"] =
                "B3161C59F88719D38E93F3B5962F03323D546746610ED9EB8B3D03DDDE1819A9",
            ["tests/acp/test_tools.py"] =
                "8D56EF349C7D14E7D6907C751FBDE2FB7A2ABBB62017160C1D1A4B71D70DAC20",
            ["tests/agent/test_api_content_sidecar.py"] =
                "F0BF151F09D806C15CA563EA22A42103768AAC3F2ADBFA82C21CC14530E37559",
            ["tests/agent/test_auxiliary_client.py"] =
                "690DD26E7A26515F2EE37A4DE19F36051F8902040DFAA65C04E975BA643F32E9",
            ["tests/agent/test_auxiliary_credential_generation.py"] =
                "226F3FA072BBD239A401B5C933053A481EED81CEE5E20A408354ECBBCA2180E4",
            ["tests/agent/test_auxiliary_runtime_context.py"] =
                "43804F35F57C8E0AE153BCB8A17E866AEC08160AFDB5885DEDBBB73FFE54C852",
            ["tests/agent/test_context_compressor.py"] =
                "A40FAD7B7D6EFE7E73F04856E32942B50024379E78CFEBA92857867187671CE0",
            ["tests/agent/test_context_engine_select_context.py"] =
                "2F2212BD91925EE680F9067DB55F685F5EA0AD3FB69E5689D80FAFA830B08528",
            ["tests/agent/test_gemini_native_adapter.py"] =
                "1343C408D43E558B9A3753840FA43602B6FD281DF6A632F525B5C612E58ECE26",
            ["tests/agent/test_identity_policy.py"] =
                "E6A6E57AB01AE47410117079A4E32F3AA628856DA76098E86E5FFBCA603191CD",
            ["tests/agent/test_isolated_request_init.py"] =
                "EE5A617761BABAF70F8F2BAD3614FE7760599C5F6088DA81573ACFF1FDE6FE09",
            ["tests/agent/test_memory_provider.py"] =
                "49C4458C6AA0D5CCE41D6A49F2693F148540C6CA6CD9E533E5077A3B93C3C006",
            ["tests/agent/test_moa_cold_start_cache_66793.py"] =
                "470702CFF71500AED00C07C33CE94110B8CFC712ACAAEDCED6F3C3A1C0B0A0B9",
            ["tests/agent/test_moa_runtime_isolation.py"] =
                "D7DC2CB41BCD8D24B911C34DA7402D72F3CF34A9DA4E02FF1C3BA33602B12578",
            ["tests/agent/test_oneshot.py"] =
                "F5B785A0DED16A480EE3B8BD35CBB4419496697E66409F4CF42A80B252BA489E",
            ["tests/agent/test_plugin_llm.py"] =
                "ACF9F9B6136EF3B2854615537862582B3E52ED243F5C3671ECFB1F9253A0DAA4",
            ["tests/agent/test_prompt_builder.py"] =
                "BFFE4D9AB7931856463B743D0EB54EC24E043624606BBE02241BADDCE6076BD4",
            ["tests/agent/test_reasoning_progress_semantics.py"] =
                "33BCA2E37A1B8B9E6B1AE539A854695F4EB83CA93D674FA8AD12F5D23E9ED859",
            ["tests/agent/test_refine_focus.py"] =
                "13B64CB3D9AB677E1B920EA602562311ADF50B78B864DF87D84689A26B3DA6A6",
            ["tests/agent/test_relay_llm.py"] =
                "E2E469CE99A065CF096A24762F4E87FEAD01A3F9378802437A929193E6487C9A",
            ["tests/agent/test_system_prompt_restore.py"] =
                "BFFFBFFAD63822DF08F2D3F7BD4C0613BEC8C8AB0B4C95C903D4806DDF3C044D",
            ["tests/agent/test_title_generator.py"] =
                "660732067A7B8662667B98E70DBAFC2C692EEC2D5AE0238DC8171F79BA41AB9C",
            ["tests/agent/test_turn_context.py"] =
                "9F9DEAD7CC4A83308870A62159ABAFB7275509368D6E12B285EB9DEDE9280547",
            ["tests/agent/test_turn_finalizer_final_response_persistence.py"] =
                "C3A54DAA50AFE167E15718B3FB38D93940A82A2F0BBBC393B65D4FBE57D9AFB1",
            ["tests/agent/test_turn_summary.py"] =
                "06ED2994AF54892C69F65CFDB57E79C74F982FE3A66D7518B3D0BF5DA23288AE",
            ["tests/cli/test_busy_input_mode_command.py"] =
                "1A36B904299AE034F3E06ABFB654E90771C50757E52753F3912E48F983729B70",
            ["tests/cli/test_cli_goal_interrupt.py"] =
                "597D92BA4D5FF59E326C1AE44E9E3C9D70BA33B0C9C60340C4E71F4B84B10AF1",
            ["tests/cli/test_cli_init.py"] =
                "16ADC942CD1BD653D9E9052D0F00ED5CCDE017CD06ED59628E7C8EDDCBC3FD40",
            ["tests/cli/test_resume_display.py"] =
                "A1F0B6D2EE02689977FB4A0C1C2CDDB246F263E056BA88C7FB9C67A200AFB900",
            ["tests/conftest.py"] =
                "98CF60EF852D828BEEE7971048954809BD15388212124ED2D9D4287AD307C8EC",
            ["tests/cron/test_local_workbench_completion.py"] =
                "0B74E9E13405A5D8987EB1780DF58301913037B255D4A85C17108226876E83AD",
            ["tests/fixtures/lilith_identity/runtime-config.yaml"] =
                "96AFFDD00AA98F1A4FBC3755AB146C4EEDE4AA2354406BAC405D39F7EBD6CC64",
            ["tests/fixtures/lilith_identity/SOUL.md"] =
                "0689620AB2247E62844BF5A80CC0DDF63145BD149171B5727B43F715730D7E7B",
            ["tests/gateway/test_bluebubbles.py"] =
                "40E994D814BF382CB41008E57E825E656FDD141C4BF6ED3E4634476FE6B07DB3",
            ["tests/gateway/test_compress_command.py"] =
                "016243EF17FAAAA521F0D546E1CCB036176931B15FE8A82DB262EA42B14509A2",
            ["tests/gateway/test_internal_notification_marker_82888.py"] =
                "86A873EE8538DF902A7FD7BD13B0E3BD2E4AD5102DB06FC9557B30A070277144",
            ["tests/gateway/test_session_hygiene.py"] =
                "02819CAE17F732CE7789E1506029BB7A2F97D570D23CF5C1ECF3CEFA73B3B891",
            ["tests/gateway/test_turn_context.py"] =
                "3D533517A10F08BE3B90D81E9B7362961D79C2A87331ABBD9B6106515392460B",
            ["tests/gateway/test_turn_lease.py"] =
                "5BAD1F2059F4DA2A22FF7B643AB7348063ED264C771FAD382DCD603B90E35BED",
            ["tests/gateway/test_whatsapp_identity.py"] =
                "5E6C6972742780062E7DFDDCE7DC90F6B85E20945609257F0236241805939D2E",
            ["tests/hermes_cli/test_auth_store_read_failure.py"] =
                "1EDEB0642535A0B5142263FB1205D7615452A9B61238263E5B02F571F30BF9F6",
            ["tests/hermes_cli/test_commands.py"] =
                "4A8E4F71145C3B938B4C7A9C724AC664AFBF02E4897876E02E5C2471395829E0",
            ["tests/hermes_cli/test_config.py"] =
                "D60F35B57B6251F825EDC4E4DC928864F514B0811DE39C82FEA3D7E37756A148",
            ["tests/hermes_cli/test_credential_lifecycle.py"] =
                "44352D55E0532168E52897732E9DA415BB6277E9FA50A67222510826AF031E2F",
            ["tests/hermes_cli/test_dashboard_admin_endpoints.py"] =
                "431D86F55EB48DCB2EB6C7977A8C1D50784CB9962146E17F1BE8C8E327BD94E5",
            ["tests/hermes_cli/test_env_loader.py"] =
                "9261E2A7449A9959001412A978D561D874DE9E937BBC4523E67353454C14A905",
            ["tests/hermes_cli/test_inventory.py"] =
                "96BC98431D4F07D694B575FA76FB23E7A8DE3EB99020DE1162FAAF8FE5FA0694",
            ["tests/hermes_cli/test_lilith_runtime_ws_attestation.py"] =
                "66984FF71FCA04CAD442F35789EC4A1F2935B98673DC774D096227B62943EE1E",
            ["tests/hermes_cli/test_managed_scope_loaders.py"] =
                "2E8AF988D643770FC4DFC85FE480BBB905FD8713E675C292D144C3C5A3F85B9C",
            ["tests/hermes_cli/test_managed_scope.py"] =
                "8B061E08E0CBB7A1EF8704C19BC2C229F57015EB5EA7BB2B5E96740DBE01344B",
            ["tests/hermes_cli/test_middleware_strict_identity.py"] =
                "41A93D1928B961D154776304F07377700BA54B2CA36F830552BA97C04F66F778",
            ["tests/hermes_cli/test_native_api_key_control.py"] =
                "FEB442C173878BBE69BD165D17DB42533878422FF2FA8EFF8336003942979219",
            ["tests/hermes_cli/test_provider_catalog.py"] =
                "20BAF0776D36F6A6FC2085E2F74FEA774D6542CE8E6B5C53290AC7543A6B09F1",
            ["tests/hermes_cli/test_skin_engine.py"] =
                "B6F73408D77B5843CA442A6A6799EA336491E5A8FD2CCAFF15B6631BD64D45F0",
            ["tests/hermes_cli/test_slack_cli.py"] =
                "46DAB21AC8E665DE6CF128442FB6792D72B357B3AB828E34CB3252DC2BDCA3B0",
            ["tests/hermes_cli/test_telegram_managed_bot.py"] =
                "EE9EB7C32353DFC1FBA1F045663534410E0572828C801105F7EF4475065F0DA4",
            ["tests/hermes_cli/test_voice_wrapper.py"] =
                "2738D47E92C359055C19E016D00C394A82D3422050344C9B51108EAA22FBE355",
            ["tests/hermes_cli/test_web_server.py"] =
                "1297616EBABF6B23114E1E6626A5BAD39C7602E85FA468055F0C9FFA7EEC18F5",
            ["tests/hermes_state/test_session_archiving.py"] =
                "11716644FDFD8D313DFF9121F879675592FC0DFF821825C9092C9E7CE99AE4B0",
            ["tests/plugins/memory/test_discovery_sources.py"] =
                "58450B61E3E7CABAD422DDCF93A1891678DB5B31F8FC3EAAD883A283395CAA64",
            ["tests/plugins/platforms/photon/test_mention_gating.py"] =
                "155398450AF006BC386F125C4981B1DB2C1033E9C7559EF39A78CBDD08EDAD6E",
            ["tests/plugins/test_a2a_plugin.py"] =
                "5C397F434B71EC12DED3609CB047E65FAC1E2DDEC4509F27ED90CD329424B31A",
            ["tests/plugins/test_google_meet_plugin.py"] =
                "609F2C53C6840025FEE702BD4BA818227165F5E9E28A8FE698D0C7F7487F2C3C",
            ["tests/plugins/test_google_meet_realtime.py"] =
                "0CD3CAAA95D5C543C174FC7D91FE23900C8ECBDEA0ACF0FDF4D2C2DD4DB454E4",
            ["tests/plugins/test_lilith_messaging_bridge.py"] =
                "7F296C0712973AF06BB3438239CA5C9457C2B60EF7D6CE781F4612B8762EAFB2",
            ["tests/run_agent/test_1630_context_overflow_loop.py"] =
                "7C48B73A9035C966A7F9654939D19C44F0CEF25120FF6FFD187F089A113A6B77",
            ["tests/run_agent/test_81641_text_turn_incremental_persistence.py"] =
                "D57D461972E1004AA8C63BA35A8E3EDDDB39B95AAF70063D99A6C37D3A8350F9",
            ["tests/run_agent/test_api_max_retries_config.py"] =
                "38BFC44A17DD97E5C4366E460FF8C51A737E049B3475B426EA992F8D154CA484",
            ["tests/run_agent/test_background_review_cache_parity.py"] =
                "B21367D58C154CDD4ABED9D5DDED58A5A3913B0957B1A5BF4B7A6F883CF5752D",
            ["tests/run_agent/test_background_review_summary.py"] =
                "9BC88171C245E646903A1796D12799E7AC4F21628127183CA2FDE1E076737B48",
            ["tests/run_agent/test_background_review.py"] =
                "A3EE1A4C5BA097BB6447DCCD9F5E3FF033DA1BB5DCA18C861341C80497DB261F",
            ["tests/run_agent/test_dropped_tool_call_recovery.py"] =
                "5136CCAF7297E727BFF1554B4DE0B9BEB725B67C3B62A89E34F6272A956D6A89",
            ["tests/run_agent/test_env_credential_turn_refresh.py"] =
                "EE6E863EF5D30B8B5F0D3DA1258A8E6F91B661BC084A39C7BA5E50D70D2D34F2",
            ["tests/run_agent/test_fallback_api_mode_preservation.py"] =
                "CD05EDA6C38AB7F21D7E5E344704D7DE9F667B61D50E17A3718FE1905182870C",
            ["tests/run_agent/test_memory_manager_close_lifecycle.py"] =
                "B25E01242319344FA42901240A995F2A7A2FA806827DBD3BFF8712A8A87FEE89",
            ["tests/run_agent/test_message_sequence_repair.py"] =
                "12B56C4DDD929D192465DA3ECA6C203C98E079BB9E54E505E63F238295AF0752",
            ["tests/run_agent/test_moa_loop_mode.py"] =
                "ADF5D36911749AF0EF9C59DC6E60EF80268AF3609091820D494DDCB26BA1217C",
            ["tests/run_agent/test_nonretryable_error_html_summary.py"] =
                "C45CCBDEB2B40A2030C30AF70F12E7B72885531B8566EBDC52841598AE61A7B9",
            ["tests/run_agent/test_review_prompt_class_first.py"] =
                "5D08058AAD1E2F76BC753C15E95EE036F7F9DF16C25753601D0F0743C8E37355",
            ["tests/run_agent/test_run_agent.py"] =
                "588B7873EB0E3919E3597E9B901B6B4454885FEAD6F8B1B4092497AB3831A201",
            ["tests/skills/test_computer_use_contract.py"] =
                "E97664B6EB663C8CE0AA0EFF2CB335A0F2CF1619BE4A71CF416ABE737ABEC829",
            ["tests/test_durable_session_lineage.py"] =
                "6D167A6FD0267ADD3252F2409A497E280A8FAC03893913497F33842A6A96A690",
            ["tests/test_iron_proxy.py"] =
                "158B8038BB1B673B5C5093508755554DE130C967E2B7BA45DF7A2E306E7B3164",
            ["tests/test_lilith_shared_session_identity.py"] =
                "B466EDC1EC2055084E6728BDFBDA22BF38B92604EAFF1B90DFCC5E2C4DAF7A60",
            ["tests/test_native_api_key_control_rpc.py"] =
                "738E4F0547C8FF15F1068A1366EE9E6B132847558FB400DAC5A3AFBE5826074A",
            ["tests/test_session_conversation_activity.py"] =
                "2F13DFD8CCD2E3E7E7FB47D76586B9FEE5DC9038DFB57F037D70BCBFC36E7408",
            ["tests/test_session_system_prompt_dedup.py"] =
                "1986A7AF477A9EC8E5A2B765ECEEEE3A432E0580C20ECEE318E0D1AB5EB8F331",
            ["tests/test_tui_gateway_server_crash_history.py"] =
                "CCE642E811E0C49025D12FADF7CE05802E927BB7FE95FAEDC6DF2E0613137F6F",
            ["tests/test_tui_gateway_server.py"] =
                "6F6F8DE0F36BBC1D0BE55E0AF76DFB7CB76512F1750448497BB6B766F8167D31",
            ["tests/tools/test_checkpoint_manager.py"] =
                "A8DACF125781F0FFDC30395F08FECF61EEB8431E01843DED00156BC739ACF05C",
            ["tests/tools/test_computer_use_approval_isolation.py"] =
                "4915F600E8D6CAED674710E01683F09841DAF976F1D25BB570A952700B84DFDF",
            ["tests/tools/test_computer_use.py"] =
                "8544A04F1AB72CECC7291A23929A33F135830DD9CB652A9D7885A2B2A3B69469",
            ["tests/tools/test_delegate.py"] =
                "45C5A4627861DEDC7034FB1DDB9803E53FDC563A30CB51208950393CB4B30D42",
            ["tests/tools/test_execute_code_approval_cluster.py"] =
                "F78BBFAD9355AC8296C9C67A77C1A70134CCC5F30A39ED2E81314D01EBEEE666",
            ["tests/tools/test_hermes_subprocess_env.py"] =
                "78B8391563A95C51920C21294359AFF169AC0D2C5908E2187AE7FD6149E45239",
            ["tests/tools/test_local_env_blocklist.py"] =
                "9389D6F8B74BF4BBD16A51854E4B55A64DF1ED15C66F49B53DE0DFA8DBDFB2E5",
            ["tests/tools/test_local_env_windows_msys.py"] =
                "89789494D4C71483D774755F38EC4CCD9EAE6B5576F566F9E9E4B88D35870000",
            ["tests/tools/test_memory_tool.py"] =
                "5284F7FB4F2E37B775DAC6B5E1C79B46E2ACF7EB5EAC78D5507F4CCFCAD81D81",
            ["tests/tools/test_request_tool_approval.py"] =
                "5B423950C8EDA88FDF27B9D6BAE2C25C59950D5C203CFC1BDD62FA5BF8E3ED1C",
            ["tests/tools/test_session_search.py"] =
                "888B7F208A3CBC3503920D8251C1D5678FA25EAD055C68D57E8059CAF376FC98",
            ["tests/tools/test_skills_tool.py"] =
                "6809657CE7A9CB2F3F0CB2FB7B3D41CAA0640E77C07AE29A5E66CDFE914AE1F8",
            ["tests/tools/test_strict_authority_postflight.py"] =
                "47F8BDBDD23B7F2F05EDFA3198FB04B515EB4F65C59F69D5C52D960CC37B312B",
            ["tests/tools/test_strict_authority_two_phase.py"] =
                "72D3EC4AB4B9DDD205FC1BE9B00A873E3328391BA5C40AED31368EEF8F1A5963",
            ["tests/tools/test_strict_identity_authority_guard.py"] =
                "D41250F4F3C7C2EB6B280DB14E074F51A57C210CDEE8B5CA726C28A68685E35C",
            ["tests/tools/test_subagent_steer.py"] =
                "F786AEE19B46BE3883BDC7523567C01C9F8BDD507377C7BBDCDDCFCC6C9FDEDD",
            ["tests/tools/test_terminal_tool.py"] =
                "1A3F86DDA15489FC06A9E83DDA2D9F099AAFABCC329392A4C6133F22154AD1AF",
            ["tests/tools/test_transcription_tools.py"] =
                "B4E922F027BEA998EBC1EE4B2ABF70B7A20E1DC7F54120F0731856E9C0D5A414",
            ["tests/tools/test_tts_opus_routing.py"] =
                "A1DDB41676CBE7A9239A824FE2F43EDE7225CF95659AE577B49D34E6A6A612FB",
            ["tests/tools/test_tts_sapi.py"] =
                "B43A15F8AE0B8538DD090921403A36F8E80E2A986FE1022BC1C02B12D841CEB4",
            ["tests/tools/test_video_analyze.py"] =
                "6C87A31E75A633B60300AC5648F6090A87BB2C50E04CCCC78D654D67B175C70F",
            ["tests/tools/test_voice_mode.py"] =
                "9F49DC3991A2F32F353B6E73929EC1AB4747D94150FD92565AB8648A48FDBF47",
            ["tests/tools/test_wake_word.py"] =
                "CDB99C64CD0FF405E493F6CBA1436F4EB48B4D6E391EC1A9F6F7431691FECB74",
            ["tests/tools/test_web_providers_ddgs.py"] =
                "8C1A6E9AC1E7A29116950FA057983F7E11FE627EAA5CA8A97456FBC231D6D7EF",
            ["tests/tools/test_write_approval.py"] =
                "7AE93C478AC92B5A6B133147E7E641135CB514BF29EBFA43C44A447C94AD7776",
            ["tests/tools/test_zombie_process_cleanup.py"] =
                "666C4729CFDA5F8BC52D691798D6806B617E3FAF682417DF68E20A525078118F",
            ["tests/tui_gateway/test_archived_session_create.py"] =
                "2CAB881FD216CAD45D9A8FFF5C84094CECB1CA66178941559C108F7013DCB150",
            ["tests/tui_gateway/test_authenticated_model_pairing.py"] =
                "ED655707CD90A9BD858D8098317944F7406B8533F675B0E8402280D5B959BC7C",
            ["tests/tui_gateway/test_compute_host_phase1.py"] =
                "92CCD59EA21638388FADF89D85C1D767B7774AA865E009B1E1EF78DBDE1B15E4",
            ["tests/tui_gateway/test_daily_note.py"] =
                "C4EBEE23935AD3FF57C1DE28042DD29610B7F964119C8D3BB1DE70211D56DF5F",
            ["tests/tui_gateway/test_delegation_session_lifecycle.py"] =
                "0ABD93C8ABD33994C7D74AD4FF942B0A97BC53E7D139CEC8F35C0FA69EEDBC2D",
            ["tests/tui_gateway/test_failed_turn_retention.py"] =
                "DA30E53DEFCEE2F92B733677172068F95FCDD80DE7A82F3B9316737B659B1872",
            ["tests/tui_gateway/test_game_current_capabilities.py"] =
                "AB891703F43201F9C6D211F623FDA5861AD6244954D945FC39718D93AAAF7948",
            ["tests/tui_gateway/test_game_display_locale.py"] =
                "BA6D3C8805988E4A5CDD8B733D3CF63F7FA9D33ABA5B1C5EEEA53D8DFCCB837A",
            ["tests/tui_gateway/test_game_native_continuity.py"] =
                "CD2890F31D1AA5DB861961985E6E5CDA85F048BB6843542CB27D5709A3E338C0",
            ["tests/tui_gateway/test_goal_command.py"] =
                "05B2A2344261F9582A09879BB6EC7A5D11482B2BA6407F0DAE52FE41FA13CA5F",
            ["tests/tui_gateway/test_gui_surface_toolsets.py"] =
                "54095691CE9776927E2C392049D6E01BFA4E3CF2B9C8CF4178E74416583C77EE",
            ["tests/tui_gateway/test_history_timestamp_projection.py"] =
                "F6D1378C60204F940F23610B76ACFDDF16408D2F0AD9F5BDD9BE7827B8B36FF2",
            ["tests/tui_gateway/test_inline_rpc_gil_starvation.py"] =
                "3E22CFF6ADE8B8A046B304C95DBA0EE84CF1D9A232E0FAA19A3E21F11B4F900E",
            ["tests/tui_gateway/test_interim_assistant_callback.py"] =
                "D173F8C5A6E455146628DBE93246477B3BFA325ABDF431999F8BC6F24C64ACE5",
            ["tests/tui_gateway/test_lilith_host_bridge.py"] =
                "F96A0F9D73EC243F60EE545E1124C89F0FC9325A67FD9133F900B8034EA201DD",
            ["tests/tui_gateway/test_lilith_noteinbox_bridge.py"] =
                "746CA85BD54F3322970E1CF4B9DE13742AA8A364E2979E22CE8B6CF68287D3A8",
            ["tests/tui_gateway/test_make_agent_personality_prompt.py"] =
                "B15AFE1D525F35EDB1196F4DA6B051E6F7E575669F0A2CFC001BC85F372E7F46",
            ["tests/tui_gateway/test_memory_provider_management_rpc.py"] =
                "29727C04437D43B61BD148D1D2E0A03E4A85E3ADC1BD5BAFA9E286075DE3E525",
            ["tests/tui_gateway/test_native_assistant_event.py"] =
                "C64100E25C376EDD493EF21FB3CEEA074F7D5B1925486A7EDCDC6660DC3E2754",
            ["tests/tui_gateway/test_native_assistant_extension.py"] =
                "9EE0CE4EF5312C60C6AC30863FE56EEF885A37A32A97A1E8112259072425321B",
            ["tests/tui_gateway/test_note_draft.py"] =
                "35B4DF3E6F3E989986194EAF8D76909F33ACF9C14075357860C4D127F5BBF8CA",
            ["tests/tui_gateway/test_personality_clobbers_system_prompt.py"] =
                "7B582850A3178D59E5E3372B3EFE56F39B38511AFED6C7E60735E81E864D38C0",
            ["tests/tui_gateway/test_profile_turn_lease.py"] =
                "E5951DD21E62AC0305C52B833153951F82B7E83A2837916EDD589119DDB2A21B",
            ["tests/tui_gateway/test_protocol.py"] =
                "F26F85CCC2AF6DF546251FA1CA6944AF1D8729D883B6FA443D9FD433BC4CF95B",
            ["tests/tui_gateway/test_reasoning_event_semantics.py"] =
                "C6F6851D8FAC73FB1F46F7F7DBD3B02EC17E1C0C2FBD0350AC4067D38CAC43FE",
            ["tests/tui_gateway/test_review_summary_callback.py"] =
                "27244DC1BE64634D7BC7FB096DDAA17284C97E2671627766B9EFFECCED5DAD02",
            ["tests/tui_gateway/test_runtime_authority_races.py"] =
                "0DD8091B072C29423B097ECC10A8A04DD468C7866A5012E5290AEB33469C4F78",
            ["tests/tui_gateway/test_speech_projection.py"] =
                "B7F8AA0F8AB6C27FBB319AEDD46BE9C9019CEC2A82585F4951EB6C2D448701BD",
            ["tests/tui_gateway/test_subagent_child_mirror.py"] =
                "D66AD98B8B98D4E2295AE56B44AD1FFFE8C3EE65C953ED159ACFA60FDBCD4B96",
            ["tests/tui_gateway/test_telegram_messaging_rpc.py"] =
                "7D7EAF721987E7644EAF965F137D5750BB9638883BBEA473EE45799BB396A95D",
            ["tests/tui_gateway/test_tools_policy_rpc.py"] =
                "4032480449C780CD5C87AEF768025191D1FCDBA64E64E150F7A89347A1C32346",
            ["tests/tui_gateway/test_tts_history_projection.py"] =
                "F2C8F37727D98D82400FAB91CE2329595A9E09B93FE56CFEB3ED53173D54CD1B",
            ["tests/tui_gateway/test_turn_scope_protocol.py"] =
                "1022F846E207F85010821E285565B329EFE8B1506B8F4793E80F837686EFC1C4",
            ["tests/tui_gateway/test_voice_capture_contract.py"] =
                "12D76A57C2B80450C92F89A191C7153FCA13324678B7FC37DB98668DE7B91524",
            ["tools/approval.py"] =
                "299784AA187259BA695CD8196444269372048C11E950528A223A10C48866B7CE",
            ["tools/checkpoint_manager.py"] =
                "A781780D6E436E54639A1098FA0F4F48C2BE4F1FF8EBABF1E39DB938C6F0370A",
            ["tools/code_execution_tool.py"] =
                "5172E294785FAA85BA26CA78E2EF4B306AF99D5E4388B1ECF988A5BFC57ACA29",
            ["tools/computer_use_tool.py"] =
                "7CE48659F337CBCBF2C2FAB781E2834859ADA18E98DC2F89FC6C75206599BD64",
            ["tools/computer_use/backend.py"] =
                "5A5762A7453FE8D2C17178C134D6359068E3F811291A68A85EB79659AEFE4B59",
            ["tools/computer_use/cua_backend.py"] =
                "589C01F8E13B08265D7A93A8ECCE8DB8786C337B947AE55CB3DFBB083F62D3D1",
            ["tools/computer_use/schema.py"] =
                "3631C4BA0D33605C9D6D06EBE80685440D18F87D321149655ABCFF2C5D9EFCE5",
            ["tools/computer_use/tool.py"] =
                "5198DB7462A9AA1ADD8DE2E031CEFEA1EA64719D258DB425F1B629C70973FECD",
            ["tools/cronjob_tools.py"] =
                "1E034A5084B63012CA12CDB2C4F059BC9B565E7421B447A1A3EAA7D86EA714A1",
            ["tools/delegate_tool.py"] =
                "4F0DBDA0484508065814DBADE76741AF3FA6F0E7E0432AD6B6D27DD14419E202",
            ["tools/environments/local.py"] =
                "7F9BA54BD9A96A795CB2204C55FD7418D95A873523F4B475011BD178A90F76E0",
            ["tools/file_tools.py"] =
                "F5300FC92DA8733648F80D6DC694F3D7F5656E70D7F3A2B7BB6F89A17C0A0510",
            ["tools/memory_tool.py"] =
                "125ADEE4555F60352B25C171B5A772F5F4F24EFD376B098019ED8BFCC6960F62",
            ["tools/process_registry.py"] =
                "B3773F4CE0BE83EE57428DC747A8C476956F32E79341F77A29AD76576F4ECA10",
            ["tools/registry.py"] =
                "27AB9333D4D5A706EA3394E824645D92CB9E3C12BB05BBF2B1776F4500468D52",
            ["tools/session_search_tool.py"] =
                "F99AC1442BF587B12C5BD88DCD0187C00742B6D3D963EDB97E34CBE517F5A994",
            ["tools/skills_tool.py"] =
                "5FB55F9F03EFB5CCB0145B526C53FB35A237C664CBF565AF68B23ED8F0177C5F",
            ["tools/terminal_tool.py"] =
                "76D83A2ED7FE94699F861AFD06C1EBBFABE4840DE845A695F56BE12BB739432D",
            ["tools/tool_backend_helpers.py"] =
                "79B2465F3FDC5AFDA0C4C6DB60D31238D176A85C5D2542EB3543D7E1A6E62030",
            ["tools/transcription_tools.py"] =
                "CB19DDC304A1219B70958C76147FA2BE3A1145F9FFBAE0D173EC53BE1F99253A",
            ["tools/tts_tool.py"] =
                "795C0A00BDDE407DA87518A9E2A3D628C33AEB25FBD80AE80205B59B2038ECF5",
            ["tools/vision_tools.py"] =
                "20FD9B708FE40750765495C601CCBC73E4A0B32E4911A5A71754EC0489368004",
            ["tools/voice_mode.py"] =
                "B9CEA0098E29C82F40E8BFE1E209525D4473068CC3DF03E22C05FF2A7F8723CC",
            ["tools/wake_word.py"] =
                "85E756E4507F19E600FAC0A4983A678CA4318766B942AF554DCC08979CDA63EF",
            ["tools/web_tools.py"] =
                "D350EEBAE84A1BE770CD37490A9EBB9479BF3EFE1ACC2C42626391C621A5988A",
            ["tools/xai_http.py"] =
                "6B83B7541EC920B67E9506B09ED389DB8072FB2A90DCFE0158409F6155625172",
            ["toolsets.py"] =
                "5046F4A1DB9FB9603B1C0A048D9402FFB070F7880C5ECC93FF804E07C9B219D7",
            ["tui_gateway/compute_host.py"] =
                "FB764A3AA10C35E26A980F726DD7FC759CB15BE7AC8A50784091E7BB2B9B55BC",
            ["tui_gateway/daily_note.py"] =
                "1898975CE1286EB90308DB74CB42B14D0EAC5FE2F9C137947D19D8E49B34C48E",
            ["tui_gateway/host_supervisor.py"] =
                "3CCC3BD90EBBA4607218E6F3CD379BEF66098362379312C746EEC7CC416EF69B",
            ["tui_gateway/lilith_host_bridge.py"] =
                "B6DB9B33001C1AE73A7699A7A5BD1D85BC922AD2479B056D225689D0CEA4CA96",
            ["tui_gateway/lilith_noteinbox_bridge.py"] =
                "92178F170F45FA64B4F2B009901FF66A991ADBBD327C963AA7FAB8EB427CFEEC",
            ["tui_gateway/methods_complete.py"] =
                "AFE9913D7E5B2615CFBA03D8FEBF13F4CBBAAF5004BB9CE31A7E7382E4BC173A",
            ["tui_gateway/methods_messaging.py"] =
                "753B16B08273C3C405395FA08996525785FCBD42F29F7B81CAF1583B33B4E15B",
            ["tui_gateway/methods_prompt.py"] =
                "C509F99AE85D127F4569A4D1706239E54E4D547D117107BA1910E625B24C40F7",
            ["tui_gateway/letters_text.py"] =
                "6FC440C2BDC2BEA337ADC28C649BDE7A86E711A76C238216237A3B82A5E3A976",
            ["tui_gateway/letters_history.py"] =
                "8CD822A68BB88B01EF7ED91EFE299419359226D47E7EE36179CF702943E4B04C",
            ["tests/tui_gateway/test_letters_text.py"] =
                "8D4B4DA7DA5E63C9C00D6052AF61E340B4EC807A91A0E2E46B074CE3CFA83ABA",
            ["tests/tui_gateway/test_letters_history.py"] =
                "3881C2E18C09BCD6FBDF49A2F938FF5C45F0AE47D1E038A16DD5C376F1E6E7B8",
            ["tui_gateway/methods_session.py"] =
                "B74EC5BF34F3CEA5F9B3D25B4031CF2539654EFA149C83E7B6CBBE8329B7BD45",
            ["tui_gateway/methods_tools.py"] =
                "444B0D0F3CB98E16B0F60126EDEA48C4F7121AC2299961BD3E4542B142AF89AC",
            ["tui_gateway/note_draft.py"] =
                "D442B541FCC44E92CEEAD5DB1C5D7DCD55B48FED045BC87E4AEA64E25085DF26",
            ["tui_gateway/server.py"] =
                "8E64836E6A8745DE42799016911A2CBD4873A8F8D33F5FA792E0E22FF70AAE01",
            ["tui_gateway/telegram_messaging.py"] =
                "22DFD955A7060FB93050C87863653808808A39BF416240C504643FBEE9D6C828",
            ["tui_gateway/ws.py"] =
                "06714D635C196C9A601EB64A2861C2D47092C8305746406A65891F7E364EC3AD",
            ["ui-tui/src/__tests__/createSlashHandler.test.ts"] =
                "58B52939A26976D05DF96F7FF02129D5B758D716D5106C8B3D3F316C5E23501F",
            ["ui-tui/src/app/slash/commands/core.ts"] =
                "E905F7968322FC8811548AB18CB3E0C9A0587961CB441F884CA4730B78573CED",
            ["ui-tui/src/app/slash/commands/ops.ts"] =
                "645C458643EC83999F4DCC50162E2AABF4C730BF24286591723342BAFE4BFA59",
            ["ui-tui/src/gatewayTypes.ts"] =
                "D765EE3B4D5C4E567E6C14F59816F7DB14FACC976D0DD331C6469F321E4FB0D9",
            ["web/src/lib/api.ts"] =
                "AD792A61FCA0F6AF83D9353ED76B40ABC7457ED0D6915A389C37498EBED88ADA",
            ["web/src/pages/ChannelsPage.tsx"] =
                "390BBD21381CC8E35AFFBC1B52F9A4693B52EEF3B9B240771F8B01F552E06BDE",
            ["tests/tools/test_process_registry_deferred_restore.py"] =
                "6C45BB95CFECCF1BA3CEE748A51A1535F35E7836A3E78ADDCE94C6384329BDD9",
            ["tests/tui_gateway/test_daily_note_approval.py"] =
                "86AA829E04497AC7437E634A831309AA6C97AA3618AE6AEEBAC5EACDE0A25758",
            ["tests/tui_gateway/test_daily_note_readiness.py"] =
                "6CE6B918754C3E252D62507DD5F902C67821728A8F8662F6CD438B8E1046D745",
            ["tui_gateway/daily_note_approval.py"] =
                "1898229CE40A433CFD10113D071A19E3DF1A5DD473ADC803746E360C7AE4F2F9",
            ["tui_gateway/daily_note_readiness.py"] =
                "FB9A51E967A49F27766092185BAC8F392F1AE3FE00301ECB2BB54EF48B5B2436",
            ["hermes_cli/providers.py"] =
                "15865E36D265AAB61FD5B62214B02373AA2A652A16B30A68DEF75D7297D53B2B",
        };

    public string HermesComponentRootPath { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "Components",
        "hermes",
        PinnedHermesComponentVersion);

    public string HermesSourcePath => Path.Combine(
        HermesComponentRootPath,
        "source");

    public string HermesSitePackagesPath => Path.Combine(
        HermesComponentRootPath,
        "site-packages");

    public string HermesExecutablePath => Path.Combine(
        HermesComponentRootPath,
        "python",
        "python.exe");

    public string HermesComponentManifestPath => Path.Combine(
        HermesComponentRootPath,
        HermesPortableComponentInstallation.ManifestFileName);

    public string CuaDriverExecutablePath { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "Components",
        "cua-driver",
        "0.21.0",
        "cua-driver.exe");

    public string VoiceComponentRootPath { get; init; } =
        PinnedHermesVoiceLayout.ResolveDefaultRootPath(AppContext.BaseDirectory);

    public string VoiceSitePackagesPath => Path.Combine(
        VoiceComponentRootPath,
        "site-packages");

    public string VoiceModelPath => Path.Combine(
        VoiceComponentRootPath,
        "models",
        "Systran",
        "faster-whisper-base",
        PinnedVoiceModelRevision);

    public string HermesHomePath { get; init; } = DefaultHermesHomePath();

    public string IdentityPackageRootPath { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "IdentityPackage",
        "v9");

    public string RelationshipMemoryPluginRootPath { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Plugins",
        "lilith_relationship");

    internal string RelationshipMemoryPluginManifestSha256 { get; init; } =
        PinnedRelationshipMemoryPluginManifestSha256;

    internal IReadOnlyList<string>
        KnownPreviousRelationshipMemoryPluginManifestSha256 { get; init; } =
            new[]
            {
                "7EF33BBF7C1F9B0054E901AED8A892E8DD52967BFB82C27D6B300436BADEADE7",
                "E3D26A81C9670213DD5FBAE2BE4E0E783CD5FADF4174EFDEC6741738AFB2969B",
                "2604ADAD6B14DDCAA474A6BB06047DD532D369C06C1B0F4982E94C92A570716E",
                "4C2C00BACC3CF0FE2BB7E33221A3416B42F5003C7CF36DB7E4796F4780122A70",
                "CDC2519B4F0EF634010EF2E9210BC37DB8428F1B611E67B0D3BB9DA979CCBA4D",
                "C3A8AADE6C1EB66FFB8AC1A0CF17DCF246877BBE280AF9B1885A0FF4DAFE0091",
                "A56CC4BA21D97DDEB3F07531981F37AC466F711245440E3BEB24C22CD8570BEF",
                "27837845F7A55D4BEC4EC457CEB0C05ADFE20BAF7530C45C10D01843468482FE",
                "7BE0ECB4738BFE8F50AE8780542DBE9B33B786E08128693E89DE8F902C9762AA",
                "7C30AF54DA01414069B102F097DC0B25E3DC70455A16AC839EA2D90A88CFE91B",
                "7972D533DB2DD9CEB4586D513CAE3A0080F47E92AA4A41B2F077433E8FF08AB5",
                "43F93CB0F8ECFC15C5283E818611C436FB5C89DEBC1E4EF10CA19B506699F971",
                "8BD78C860ECC7746136ECFE0A5781ED21CD2C04D951FC78A830A9E6F7FFA4338",
                "E6EBEF2972A5CCFFBD62F64A6BBA39136C961425B7353A4B2BB47D44D1E8550A",
                "F69947FDB97EEDB30203CA5FA445F29613A1AFDDA254F87E7CA454BBFD0CD243",
                "A056940B4AE5B81A9BB6082A7D9BA31CE44A1513B8AF60610B7242595F48FCCD",
                "F361377EBF096676E9C9E8FFA1CE8E350D6A9841FBB88CB7A32AB233D9BBED32",
                "3867997B1E2418C0C5A223D65C9B549D58FD22389D5C7354F3DB4757E4CB3E52",
                "CF4FDE1EA684577814E12506EA2962B9A6F01C1467AD6F6E47E7A616886A818D",
                "280C3AF70712676559BC36762939BA8C930F79542E14E6D1D90BBC0DE2A84987",
                "1859B6E75CFAEB4CBD1F1D8F5D252F72B993F12F8DC8E9C3F190077A329E43D5",
                "FED14C81CFB8E4FEF8662F6D64850FB0C610AD9D71D3E490F2223E3760D45CEA",
                "7E0EC2D9B5FE26C39A1303C6930ACB8206013B18227E848D738110914A01D90E",
                "11BF3083A1D10C05413A1118319E88D9F21435B9DC850013000F46E024E77F9C",
                "0AF3D28F1A9E789EA1546ADB4AC3A06BBC92C105C592BFD4CAEC88D1D8CF6488",
                "BD43A9E6331A16941E8580BD31BC4E23C5FD7D074F520B6A0D86334EE66A2936",
                "A68DD1B0A038B4F8E738150907A3C3ED0A8C881006D0417A0416E05F74DBED02",
                "52CF333DC8D89C3E8993DA2D16AC657B4FC7961C38FB6BFB30B5F32D709F2019",
                "91B92A99E23853C3A498AF59A2314F8E2AABC70FAC41CF32D2E0836FEE966A58",
            };

    public string NotesPluginRootPath { get; init; } = Path.Combine(
        AppContext.BaseDirectory,
        "Assets",
        "Plugins",
        LilithNotesPluginProvisioner.PluginId);

    internal string NotesPluginManifestSha256 { get; init; } =
        PinnedNotesPluginManifestSha256;

    internal IReadOnlyList<string> KnownPreviousNotesPluginManifestSha256
        { get; init; } = new[]
        {
            "2D7CCB520EF8D07CAD96F9A4AE0DE02BEECE7B2F844BAAA5B124518C5F225782",
            "DCBA81FB7D4EB53BFDA6ED022119629CB2EF4DF67CE1C1E2C97BC185497DD4B8"
        };

    public string LeaveNotePluginRootPath { get; init; } = Path.Combine(
        AppContext.BaseDirectory, "Assets", "Plugins",
        LilithLeaveNotePluginProvisioner.PluginId);

    internal string LeaveNotePluginManifestSha256 { get; init; } =
        PinnedLeaveNotePluginManifestSha256;

    internal IReadOnlyList<string> KnownPreviousLeaveNotePluginManifestSha256
        { get; init; } = new[]
        {
            "0B11840C73A5223E148844B1D5F3C511F8797F68E9C3F895A5736FAD4D543555"
        };

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(90);

    public TimeSpan GatewayRequestTimeout { get; init; } =
        TimeSpan.FromSeconds(30);

    // Speech projection requests a 60-second provider timeout. Give a normal
    // attempt room to return without extending other RPCs; overall waiting
    // remains bounded even if provider recovery takes longer.
    public TimeSpan SpeechProjectionRequestTimeout { get; init; } =
        TimeSpan.FromSeconds(75);

    public TimeSpan AgentBuildTimeout { get; init; } =
        TimeSpan.FromSeconds(90);

    public TimeSpan ShutdownGracePeriod { get; init; } =
        TimeSpan.FromSeconds(2);

    public static LilithRuntimeOptions CreateDefault() => new();

    internal TimeSpan GetGatewayRequestTimeout(string method) => method switch
    {
        "lilith.daily_note.prepare" => TimeSpan.FromSeconds(35),
        "lilith.daily_note.evaluate" => TimeSpan.FromSeconds(65),
        "lilith.daily_note.complete" or "lilith.daily_note.cancel" or "lilith.daily_note.prepare.cancel" =>
            GatewayRequestTimeout < TimeSpan.FromSeconds(4)
                ? GatewayRequestTimeout : TimeSpan.FromSeconds(4),
        "prompt.project_speech" => SpeechProjectionRequestTimeout,
        _ => GatewayRequestTimeout
    };

    public static string DefaultHermesHomePath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException(
                "The current user LocalApplicationData path is unavailable.");
        }

        return Path.Combine(
            localAppData,
            "LilithAiMod",
            "Runtime",
            "hermes",
            "lilith-core-3c5fd918");
    }

    internal void Validate()
    {
        RequireAbsolutePath(
            HermesComponentRootPath,
            nameof(HermesComponentRootPath));
        RequireAbsolutePath(
            CuaDriverExecutablePath,
            nameof(CuaDriverExecutablePath));
        RequireAbsolutePath(
            VoiceComponentRootPath,
            nameof(VoiceComponentRootPath));
        RequireAbsolutePath(HermesHomePath, nameof(HermesHomePath));
        RequireAbsolutePath(
            IdentityPackageRootPath,
            nameof(IdentityPackageRootPath));
        RequireAbsolutePath(
            RelationshipMemoryPluginRootPath,
            nameof(RelationshipMemoryPluginRootPath));
        RequireSha256(
            RelationshipMemoryPluginManifestSha256,
            nameof(RelationshipMemoryPluginManifestSha256));
        ArgumentNullException.ThrowIfNull(
            KnownPreviousRelationshipMemoryPluginManifestSha256);
        var previousDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var digest in KnownPreviousRelationshipMemoryPluginManifestSha256)
        {
            RequireSha256(
                digest,
                nameof(KnownPreviousRelationshipMemoryPluginManifestSha256));
            if (string.Equals(
                    digest,
                    RelationshipMemoryPluginManifestSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !previousDigests.Add(digest))
            {
                throw new ArgumentException(
                    "Trusted previous relationship-memory manifests must be unique and older than the current manifest.",
                    nameof(KnownPreviousRelationshipMemoryPluginManifestSha256));
            }
        }

        RequireAbsolutePath(
            NotesPluginRootPath,
            nameof(NotesPluginRootPath));
        RequireSha256(
            NotesPluginManifestSha256,
            nameof(NotesPluginManifestSha256));
        ArgumentNullException.ThrowIfNull(
            KnownPreviousNotesPluginManifestSha256);
        var previousNotesDigests = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var digest in KnownPreviousNotesPluginManifestSha256)
        {
            RequireSha256(
                digest,
                nameof(KnownPreviousNotesPluginManifestSha256));
            if (string.Equals(
                    digest,
                    NotesPluginManifestSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !previousNotesDigests.Add(digest))
            {
                throw new ArgumentException(
                    "Trusted previous Lilith notes manifests must be unique and older than the current manifest.",
                    nameof(KnownPreviousNotesPluginManifestSha256));
            }
        }

        RequireAbsolutePath(LeaveNotePluginRootPath, nameof(LeaveNotePluginRootPath));
        RequireSha256(LeaveNotePluginManifestSha256, nameof(LeaveNotePluginManifestSha256));
        ArgumentNullException.ThrowIfNull(KnownPreviousLeaveNotePluginManifestSha256);
        var previousLeaveNoteDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var digest in KnownPreviousLeaveNotePluginManifestSha256)
        {
            RequireSha256(digest, nameof(KnownPreviousLeaveNotePluginManifestSha256));
            if (string.Equals(digest, LeaveNotePluginManifestSha256, StringComparison.OrdinalIgnoreCase)
                || !previousLeaveNoteDigests.Add(digest))
            {
                throw new ArgumentException(
                    "Trusted previous image-note manifests must be unique and older than the current manifest.",
                    nameof(KnownPreviousLeaveNotePluginManifestSha256));
            }
        }

        RequirePositive(StartupTimeout, nameof(StartupTimeout));
        RequirePositive(GatewayRequestTimeout, nameof(GatewayRequestTimeout));
        if (SpeechProjectionRequestTimeout <= TimeSpan.FromSeconds(60)
            || SpeechProjectionRequestTimeout > TimeSpan.FromSeconds(120))
        {
            throw new ArgumentOutOfRangeException(
                nameof(SpeechProjectionRequestTimeout),
                "Speech projection must allow more than 60 seconds and at most 120 seconds.");
        }
        RequirePositive(AgentBuildTimeout, nameof(AgentBuildTimeout));
        RequirePositive(ShutdownGracePeriod, nameof(ShutdownGracePeriod));
    }

    private static void RequireAbsolutePath(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException(
                "A non-empty absolute path is required.",
                parameter);
        }
    }

    private static void RequirePositive(TimeSpan value, string parameter)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }

    private static void RequireSha256(string value, string parameter)
    {
        if (value.Length != 64
            || value.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F')
                and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "A SHA-256 digest is required.",
                parameter);
        }
    }
}
