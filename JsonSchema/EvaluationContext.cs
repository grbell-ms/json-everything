﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Json.Pointer;

namespace Json.Schema;

/// <summary>
/// Provides a single source of data for evaluation operations.
/// </summary>
public class EvaluationContext
{
	private JsonSchema? _currentAnchorBackup;

	private readonly Stack<Uri> _currentUris = new();
	private readonly Stack<JsonNode?> _localInstances = new();
	private readonly Stack<JsonPointer> _instanceLocations = new();
	private readonly Stack<JsonSchema> _localSchemas = new();
	private readonly Stack<JsonPointer> _evaluationPaths = new();
	private readonly Stack<EvaluationResults> _localResults = new();
	private readonly Stack<bool> _dynamicScopeFlags = new();
	private readonly Stack<IReadOnlyDictionary<Uri, bool>?> _metaSchemaVocabs = new();
	private readonly Stack<bool> _directRefNavigation = new();
	private readonly Stack<bool> _requireAnnotations = new();
	private JsonSchema? _currentAnchor;

	/// <summary>
	/// The option set for the evaluation.
	/// </summary>
	public EvaluationOptions Options { get; }

	/// <summary>
	/// The root schema.
	/// </summary>
	public JsonSchema SchemaRoot { get; }

	/// <summary>
	/// The current subschema location relative to the schema root.
	/// </summary>
	public JsonPointer EvaluationPath => _evaluationPaths.Peek();

	/// <summary>
	/// The current subschema.
	/// </summary>
	public JsonSchema LocalSchema => _localSchemas.Peek();
	/// <summary>
	/// The instance root.
	/// </summary>
	public JsonNode? InstanceRoot { get; }

	/// <summary>
	/// The current instance location relative to the instance root.
	/// </summary>
	public JsonPointer InstanceLocation => _instanceLocations.Peek();

	/// <summary>
	/// The current instance.
	/// </summary>
	public JsonNode? LocalInstance => _localInstances.Peek();

	/// <summary>
	/// The current URI, based on `$id` and `$anchor` keywords present in the schema.
	/// </summary>
	public Uri CurrentUri => _currentUris.Peek();

	/// <summary>
	/// The current URI anchor.
	/// </summary>
	public JsonSchema? CurrentAnchor
	{
		get => _currentAnchor;
		internal set
		{
			_currentAnchorBackup = _currentAnchor ?? value;
			_currentAnchor = value;
		}
	}

	/// <summary>
	/// The result object for the current evaluation.
	/// </summary>
	public EvaluationResults LocalResult => _localResults.Peek();

	internal bool UriChanged { get; set; }
	internal JsonPointer? Reference { get; set; }

	internal IReadOnlyDictionary<Uri, bool>? MetaSchemaVocabs => _metaSchemaVocabs.Peek();

	internal bool IsNewDynamicScope => _dynamicScopeFlags.Peek();
	internal HashSet<(string, JsonPointer)> NavigatedReferences { get; } = new();
	internal bool NavigatedByDirectRef
	{
		get => _directRefNavigation.Peek();
		set
		{
			_directRefNavigation.Pop();
			_directRefNavigation.Push(value);
		}
	}

	/// <summary>
	/// Whether processing optimizations can be applied (output format = flag).
	/// </summary>
	public bool ApplyOptimizations => Options.OutputFormat == OutputFormat.Flag && !_requireAnnotations.Peek();

#pragma warning disable 8618
	internal EvaluationContext(EvaluationOptions options,
		Uri? currentUri,
		JsonNode? instanceRoot,
		JsonSchema schemaRoot)
	{
		Options = options;
		_currentUris.Push(currentUri ?? options.DefaultBaseUri);
		InstanceRoot = instanceRoot;
		SchemaRoot = schemaRoot;
		_localInstances.Push(instanceRoot);
		_instanceLocations.Push(JsonPointer.Empty);
		_localSchemas.Push(schemaRoot);
		_evaluationPaths.Push(JsonPointer.Empty);
		_localResults.Push(new EvaluationResults(this));
		_dynamicScopeFlags.Push(false);
		_metaSchemaVocabs.Push(null);
		_directRefNavigation.Push(false);
		_requireAnnotations.Push(RequiresAnnotationCollection(schemaRoot));
	}
#pragma warning restore 8618

	/// <summary>
	/// Pushes the state onto the stack and sets up for a nested layer of evaluation.
	/// </summary>
	/// <param name="instanceLocation">The location within the data instance root.</param>
	/// <param name="instance">The data instance.</param>
	/// <param name="evaluationPath">The location within the schema root.</param>
	/// <param name="subschema">The subschema.</param>
	/// <param name="newUri">The URI of the subschema.</param>
	public void Push(in JsonPointer instanceLocation,
		in JsonNode? instance,
		in JsonPointer evaluationPath,
		in JsonSchema subschema,
		Uri? newUri = null)
	{
		_currentUris.Push(newUri ?? CurrentUri);
		_instanceLocations.Push(instanceLocation);
		_localInstances.Push(instance);
		_evaluationPaths.Push(evaluationPath);
		_localSchemas.Push(subschema);
		var newResult = new EvaluationResults(this);
		LocalResult.AddNestedResult(newResult);
		_localResults.Push(newResult);
		_dynamicScopeFlags.Push(false);
		_metaSchemaVocabs.Push(_metaSchemaVocabs.Peek());
		_directRefNavigation.Push(false);
	}

	/// <summary>
	/// Pushes the state onto the stack and sets up for a nested layer of evaluation.
	/// </summary>
	/// <param name="evaluationPath">The location within the schema root.</param>
	/// <param name="subschema">The subschema.</param>
	/// <param name="newUri">The URI of the subschema.</param>
	public void Push(in JsonPointer evaluationPath,
		in JsonSchema subschema,
		Uri? newUri = null)
	{
		_currentUris.Push(newUri ?? CurrentUri);
		_instanceLocations.Push(InstanceLocation);
		_localInstances.Push(LocalInstance);
		_evaluationPaths.Push(evaluationPath);
		_localSchemas.Push(subschema);
		_requireAnnotations.Push(_requireAnnotations.Peek() || RequiresAnnotationCollection(localSubschema));
		var newResult = new EvaluationResults(this);
		LocalResult.AddNestedResult(newResult);
		_localResults.Push(newResult);
		_dynamicScopeFlags.Push(false);
		_metaSchemaVocabs.Push(_metaSchemaVocabs.Peek());
		_directRefNavigation.Push(false);
	}

	/// <summary>
	/// Evaluates as a subschema.  To be called from within keywords.
	/// </summary>
	public void Evaluate()
	{
		if (LocalSchema.BoolValue.HasValue)
		{
			this.Log(() => $"Found {(LocalSchema.BoolValue.Value ? "true" : "false")} schema: {LocalSchema.BoolValue.Value.GetValidityString()}");
			if (!LocalSchema.BoolValue.Value)
				LocalResult.Fail(string.Empty, ErrorMessages.FalseSchema);
			return;
		}

		var metaSchemaUri = LocalSchema.Keywords!.OfType<SchemaKeyword>().FirstOrDefault()?.Schema;
		var keywords = Options.FilterKeywords(LocalSchema.Keywords!, metaSchemaUri, Options.SchemaRegistry);

		HashSet<Type>? keywordTypesToProcess = null;
		foreach (var keyword in keywords.OrderBy(k => k.Priority()))
		{
			// $schema is always processed first, and this should only be set
			// after $schema has been evaluated.
			if (keyword is not SchemaKeyword && !Options.ProcessCustomKeywords)
				keywordTypesToProcess ??= GetKeywordsToProcess();
			if (!keywordTypesToProcess?.Contains(keyword.GetType()) ?? false) continue;

			keyword.Evaluate(this);

			if (!LocalResult.IsValid && ApplyOptimizations) break;
		}

		if (IsNewDynamicScope)
			Options.SchemaRegistry.ExitingUriScope();
	}

	private bool RequiresAnnotationCollection(JsonSchema schema)
	{
		return schema.Keywords?.Any(x => x.GetType() == typeof(UnevaluatedPropertiesKeyword) ||
										 x.GetType() == typeof(UnevaluatedItemsKeyword)) ?? false;
	}

	/// <summary>
	/// Pops the state from the stack to return to a previous layer of evaluation.
	/// </summary>
	public void Pop()
	{
		_currentUris.Pop();
		_instanceLocations.Pop();
		_localInstances.Pop();
		_evaluationPaths.Pop();
		_localSchemas.Pop();
		_localResults.Pop();
		_dynamicScopeFlags.Pop();
		_metaSchemaVocabs.Pop();
		_directRefNavigation.Pop();
	}

	internal void UpdateCurrentUri(Uri newUri)
	{
		UpdateCurrentValue(_currentUris, newUri);
		UpdateCurrentValue(_dynamicScopeFlags, true);
	}

	internal void UpdateMetaSchemaVocabs(IReadOnlyDictionary<Uri, bool> newVocabSet)
	{
		UpdateCurrentValue(_metaSchemaVocabs, newVocabSet);
	}

	private static void UpdateCurrentValue<T>(Stack<T> stack, T newValue)
	{
		stack.Pop();
		stack.Push(newValue);
	}

	internal void ValidateAnchor()
	{
		CurrentAnchor = _currentAnchorBackup;
	}

	private HashSet<Type>? GetKeywordsToProcess()
	{
		return MetaSchemaVocabs == null
			? null
			: new HashSet<Type>(MetaSchemaVocabs.Keys
				.SelectMany(x => Options.VocabularyRegistry.Get(x)?.Keywords ??
								 Enumerable.Empty<Type>()));
	}
}