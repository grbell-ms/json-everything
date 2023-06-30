﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Json.More;

namespace Json.Schema;

/// <summary>
/// Handles `additionalProperties`.
/// </summary>
[SchemaPriority(10)]
[SchemaKeyword(Name)]
[SchemaSpecVersion(SpecVersion.Draft6)]
[SchemaSpecVersion(SpecVersion.Draft7)]
[SchemaSpecVersion(SpecVersion.Draft201909)]
[SchemaSpecVersion(SpecVersion.Draft202012)]
[SchemaSpecVersion(SpecVersion.DraftNext)]
[Vocabulary(Vocabularies.Applicator201909Id)]
[Vocabulary(Vocabularies.Applicator202012Id)]
[Vocabulary(Vocabularies.ApplicatorNextId)]
[DependsOnAnnotationsFrom(typeof(PropertiesKeyword))]
[DependsOnAnnotationsFrom(typeof(PatternPropertiesKeyword))]
[JsonConverter(typeof(AdditionalPropertiesKeywordJsonConverter))]
public class AdditionalPropertiesKeyword : IJsonSchemaKeyword, ISchemaContainer, IEquatable<AdditionalPropertiesKeyword>
{
	/// <summary>
	/// The JSON name of the keyword.
	/// </summary>
	public const string Name = "additionalProperties";

	/// <summary>
	/// The schema by which to evaluate additional properties.
	/// </summary>
	public JsonSchema Schema { get; }

	/// <summary>
	/// Creates a new <see cref="AdditionalPropertiesKeyword"/>.
	/// </summary>
	/// <param name="value">The keyword's schema.</param>
	public AdditionalPropertiesKeyword(JsonSchema value)
	{
		Schema = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>
	/// Performs evaluation for the keyword.
	/// </summary>
	/// <param name="context">Contextual details for the evaluation process.</param>
	/// <param name="token">The cancellation token used by the caller.</param>
	public async Task Evaluate(EvaluationContext context, CancellationToken token)
	{
		context.EnterKeyword(Name);
		var schemaValueType = context.LocalInstance.GetSchemaValueType();
		if (schemaValueType != SchemaValueType.Object)
		{
			context.WrongValueKind(schemaValueType);
			return;
		}

		context.Options.LogIndentLevel++;
		var overallResult = true;
		List<string> evaluatedProperties;
		var obj = (JsonObject)context.LocalInstance!;
		if (!obj.VerifyJsonObject()) return;

		if (context.Options.EvaluatingAs is SpecVersion.Draft6 or SpecVersion.Draft7)
		{
			evaluatedProperties = new List<string>();
			if (context.LocalSchema.TryGetKeyword<PropertiesKeyword>(PropertiesKeyword.Name, out var propertiesKeyword))
				evaluatedProperties.AddRange(propertiesKeyword!.Properties.Keys);
			if (context.LocalSchema.TryGetKeyword<PatternPropertiesKeyword>(PatternPropertiesKeyword.Name, out var patternPropertiesKeyword))
				evaluatedProperties.AddRange(obj
					.Select(x => x.Key)
					.Where(x => patternPropertiesKeyword!.Patterns.Any(p => p.Key.IsMatch(x))));
		}
		else
		{
			if (!context.LocalResult.TryGetAnnotation(PropertiesKeyword.Name, out var annotation))
			{
				context.Log(() => $"No annotation from {PropertiesKeyword.Name}.");
				evaluatedProperties = new List<string>();
			}
			else
			{
				// ReSharper disable once AccessToModifiedClosure
				context.Log(() => $"Annotation from {PropertiesKeyword.Name}: {annotation.AsJsonString()}");
				evaluatedProperties = annotation!.AsArray().Select(x => x!.GetValue<string>()).ToList();
			}
			if (!context.LocalResult.TryGetAnnotation(PatternPropertiesKeyword.Name, out annotation))
				context.Log(() => $"No annotation from {PatternPropertiesKeyword.Name}.");
			else
			{
				context.Log(() => $"Annotation from {PatternPropertiesKeyword.Name}: {annotation.AsJsonString()}");
				evaluatedProperties.AddRange(annotation!.AsArray().Select(x => x!.GetValue<string>()));
			}
		}
		var additionalProperties = obj.Where(p => !evaluatedProperties.Contains(p.Key)).ToArray();
		evaluatedProperties.Clear();

		var tokenSource = new CancellationTokenSource();
		token.Register(tokenSource.Cancel);

		var tasks = additionalProperties.Select(property =>
		{
			if (tokenSource.Token.IsCancellationRequested) return Task.FromResult(((string?)null, (bool?)null));

			if (!obj.TryGetPropertyValue(property.Key, out var item))
			{
				context.Log(() => $"Property '{property.Key}' does not exist. Skipping.");
				return Task.FromResult(((string?)null, (bool?)null));
			}

			context.Log(() => $"Evaluating property '{property.Key}'.");
			var branch = context.ParallelBranch(context.InstanceLocation.Combine(property.Key),
				item ?? JsonNull.SignalNode,
				context.EvaluationPath.Combine(Name),
				Schema);
			return Task.Run(async () =>
			{
				await branch.Evaluate(tokenSource.Token);
				var localResult = branch.LocalResult.IsValid;
				context.Log(() => $"Property '{property.Key}' {localResult.GetValidityString()}.");

				return ((string?)property.Key, (bool?)localResult);
			}, tokenSource.Token);
		}).ToArray();

		if (tasks.Any())
		{
			if (context.ApplyOptimizations)
			{
				var failedValidation = await tasks.WhenAny(x => !x.Item2 ?? false, tokenSource.Token);
				tokenSource.Cancel();

				overallResult = failedValidation == null;
			}
			else
			{
				await Task.WhenAll(tasks);
				overallResult = tasks.All(x => x.Result.Item2 ?? true);
			}
		}

		context.Options.LogIndentLevel--;
		evaluatedProperties.AddRange(tasks.Select(x => x.Result.Item1!).Where(x => x != null));
		context.LocalResult.SetAnnotation(Name, JsonSerializer.SerializeToNode(evaluatedProperties));

		if (!overallResult)
			context.LocalResult.Fail();
		context.ExitKeyword(Name, context.LocalResult.IsValid);
	}

	/// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
	/// <param name="other">An object to compare with this object.</param>
	/// <returns>true if the current object is equal to the <paramref name="other">other</paramref> parameter; otherwise, false.</returns>
	public bool Equals(AdditionalPropertiesKeyword? other)
	{
		if (ReferenceEquals(null, other)) return false;
		if (ReferenceEquals(this, other)) return true;
		return Equals(Schema, other.Schema);
	}

	/// <summary>Determines whether the specified object is equal to the current object.</summary>
	/// <param name="obj">The object to compare with the current object.</param>
	/// <returns>true if the specified object  is equal to the current object; otherwise, false.</returns>
	public override bool Equals(object obj)
	{
		return Equals(obj as AdditionalPropertiesKeyword);
	}

	/// <summary>Serves as the default hash function.</summary>
	/// <returns>A hash code for the current object.</returns>
	public override int GetHashCode()
	{
		return Schema.GetHashCode();
	}
}

internal class AdditionalPropertiesKeywordJsonConverter : JsonConverter<AdditionalPropertiesKeyword>
{
	public override AdditionalPropertiesKeyword Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		var schema = JsonSerializer.Deserialize<JsonSchema>(ref reader, options)!;

		return new AdditionalPropertiesKeyword(schema);
	}
	public override void Write(Utf8JsonWriter writer, AdditionalPropertiesKeyword value, JsonSerializerOptions options)
	{
		writer.WritePropertyName(AdditionalPropertiesKeyword.Name);
		JsonSerializer.Serialize(writer, value.Schema, options);
	}
}