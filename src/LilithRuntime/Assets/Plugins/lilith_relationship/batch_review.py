"""Batch policy bridge: only the native host's opaque captured context is read."""
from .domain import RelationshipContractError, review_json_schema


def batch_interval(context):
    getter = getattr(context, "_materialize_usage_origin_for_host", None)
    if not callable(getter):
        return 1
    origin = getter()
    if origin is None:
        return 1
    spec = getattr(origin.ledger.policy, "information_strategy", None)
    if spec is None:
        return 1  # native104 and earlier keep their tested single-turn path.
    value = spec.relationship_every
    if type(value) is not int or not 1 <= value <= 8:
        raise RelationshipContractError("invalid relationship batch cadence")
    return value


def batch_route_stopped(context):
    getter = getattr(context, "_materialize_usage_origin_for_host", None)
    origin = getter() if callable(getter) else None
    return origin is not None and origin.ledger.snapshot().stopped


def batch_schema(count):
    return {"type": "object", "additionalProperties": False, "required": ["reviews"],
            "properties": {"reviews": {"type": "array", "minItems": count, "maxItems": count,
                "items": {"type": "object", "additionalProperties": False,
                    "required": ["turn", "review"], "properties": {
                        "turn": {"type": "integer", "minimum": 0, "maximum": count-1},
                        "review": review_json_schema()}}}}}


def ordered_reviews(parsed, count):
    if not isinstance(parsed, dict) or set(parsed) != {"reviews"}:
        raise RelationshipContractError("invalid batch result")
    items = parsed["reviews"]
    if not isinstance(items, list) or len(items) != count:
        raise RelationshipContractError("batch must account for every source turn")
    bound = {}
    for item in items:
        if (not isinstance(item, dict) or set(item) != {"turn", "review"}
                or type(item["turn"]) is not int or not 0 <= item["turn"] < count
                or item["turn"] in bound):
            raise RelationshipContractError("unknown or duplicate batch source")
        bound[item["turn"]] = item["review"]
    return tuple(bound[i] for i in range(count))
