﻿using System.Text.Json;
using Json.More;

namespace Json.Logic.Rules;

[Operator("!!")]
internal class BooleanCastRule : Rule
{
	private readonly Rule _value;

	public BooleanCastRule(Rule value)
	{
		_value = value;
	}

	public override JsonElement Apply(JsonElement data, JsonElement? contextData = null)
	{
		var value = _value.Apply(data, contextData);

		if (value.ValueKind == JsonValueKind.Object)
			throw new JsonLogicException("Cannot cast objects to boolean");

		return _value.Apply(data, contextData).IsTruthy().AsJsonElement();
	}
}