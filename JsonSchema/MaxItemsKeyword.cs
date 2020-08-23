﻿using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Json.Schema
{
	[SchemaKeyword(Name)]
	[SchemaDraft(Draft.Draft6)]
	[SchemaDraft(Draft.Draft7)]
	[SchemaDraft(Draft.Draft201909)]
	[Vocabulary(VocabularyRegistry.Validation201909Id)]
	[JsonConverter(typeof(MaxItemsKeywordJsonConverter))]
	public class MaxItemsKeyword : IJsonSchemaKeyword
	{
		internal const string Name = "maxItems";

		public uint Value { get; }

		public MaxItemsKeyword(uint value)
		{
			Value = value;
		}

		public void Validate(ValidationContext context)
		{
			if (context.LocalInstance.ValueKind != JsonValueKind.Array)
			{
				context.IsValid = true;
				return;
			}

			var number = context.LocalInstance.GetArrayLength();
			context.IsValid = Value >= number;
			if (!context.IsValid)
				context.Message = $"Value has more than {Value} items";
		}
	}

	public class MaxItemsKeywordJsonConverter : JsonConverter<MaxItemsKeyword>
	{
		public override MaxItemsKeyword Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.Number)
				throw new JsonException("Expected number");

			var number = reader.GetUInt32();

			return new MaxItemsKeyword(number);
		}
		public override void Write(Utf8JsonWriter writer, MaxItemsKeyword value, JsonSerializerOptions options)
		{
			writer.WriteNumber(MaxItemsKeyword.Name, value.Value);
		}
	}
}