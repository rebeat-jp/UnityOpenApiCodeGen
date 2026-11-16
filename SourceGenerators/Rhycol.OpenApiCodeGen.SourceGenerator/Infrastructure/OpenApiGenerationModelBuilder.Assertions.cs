using System;
using System.Collections.Generic;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// Maps the assertion trees of the semantic schemas to roots of one assertion graph. A type or a DTO carries
    /// the names of its roots; the graph is emitted once, next to the constraint helpers of the client.
    /// </summary>
    internal sealed partial class OpenApiGenerationModelBuilder
    {
        private readonly GeneratedAssertionGraphBuilder _assertionGraph = new GeneratedAssertionGraphBuilder();
        // Root name per tree ("" when the tree accepts everything); a tree is registered once however often it is used.
        private readonly Dictionary<OpenApiSemanticAssertion, string> _assertionRoots =
            new Dictionary<OpenApiSemanticAssertion, string>();
        private readonly List<OpenApiSemanticAssertion> _assertionOrder = new List<OpenApiSemanticAssertion>();

        private IReadOnlyList<string> AssertionRootsOf(OpenApiSemanticSchema schema)
        {
            if (schema.Assertions.Count == 0)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (OpenApiSemanticAssertion assertion in schema.Assertions)
            {
                if (!_assertionRoots.TryGetValue(assertion, out string? name))
                {
                    name = "S" + _assertionRoots.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (_assertionGraph.AddRoot(name, assertion) < 0)
                    {
                        name = string.Empty;
                    }

                    _assertionRoots.Add(assertion, name);
                    _assertionOrder.Add(assertion);
                }

                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }

            return names;
        }

        // A DTO keeps its roots in its model (a recursive schema stays finite); every other type carries them itself.
        private GeneratedTypeModel AttachAssertions(GeneratedTypeModel type, OpenApiSemanticSchema schema)
        {
            if (schema.Assertions.Count == 0 ||
                schema.Kind == OpenApiSemanticSchemaKind.Object ||
                type.Kind == GeneratedTypeKind.File)
            {
                return type;
            }

            return type.WithAssertions(AssertionRootsOf(schema));
        }

        private GeneratedAssertionGraph? BuildAssertionGraph()
        {
            if (_assertionRoots.Count == 0)
            {
                return null;
            }

            try
            {
                return _assertionGraph.Build();
            }
            catch (GeneratedAssertionCycleException exception)
            {
                OpenApiSemanticAssertion origin = _assertionOrder[0];
                throw new OpenApiSemanticException(
                    OpenApiSemanticErrorKind.InvalidDocument,
                    exception.Message,
                    origin.Location ?? _document.Location,
                    Array.Empty<OpenApiSourceLocation>());
            }
        }
    }
}
