using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    /// <summary>
    /// P7 (G31): OpenAPI elements that describe something other than a client request (webhooks, callbacks,
    /// links) are accepted with an OACG108 warning instead of being rejected.
    /// Ignored subtrees are never parsed: only the kind of the node itself is checked, so a $ref inside
    /// them (unresolved, external, or pointing at an unsupported construct) cannot raise an error.
    /// Schemas used only from an ignored subtree get no DTO unless they are also declared in
    /// components.schemas, which is always generated.
    /// Documentation-only elements (tags, externalDocs, example, examples, xml, x- extensions,
    /// components.examples) are accepted silently, like description.
    /// </summary>
    internal sealed partial class OpenApiSemanticParser
    {
        // Root 'webhooks' (OpenAPI 3.1+, but tolerated for any version): no client methods are generated.
        private void WarnIgnoredWebhooks()
        {
            SpecNode? webhooks = GetProperty(_root, "webhooks");
            if (webhooks is not null)
            {
                WarnIgnoredMap(
                    webhooks,
                    "webhooks",
                    "OpenAPI webhooks are incoming requests, not client calls; they were ignored and no client method is generated.");
            }
        }

        // Operation 'callbacks'.
        private void WarnIgnoredCallbacks(SpecNode operationNode)
        {
            SpecNode? callbacks = GetProperty(operationNode, "callbacks");
            if (callbacks is not null)
            {
                WarnIgnoredMap(
                    callbacks,
                    "callbacks",
                    "OpenAPI callbacks are requests sent by the server, not client calls; they were ignored and no client method is generated.");
            }
        }

        // Response 'links'.
        private void WarnIgnoredLinks(SpecNode responseNode)
        {
            SpecNode? links = GetProperty(responseNode, "links");
            if (links is not null)
            {
                WarnIgnoredMap(
                    links,
                    "links",
                    "OpenAPI response links describe follow-up operations and were ignored; no client member is generated for them.");
            }
        }

        // components.links / components.callbacks warn, components.examples is documentation only.
        private bool TryAcceptIgnoredComponentSection(SpecProperty section)
        {
            switch (section.Name)
            {
                case "examples":
                    RequireKind(section.Value, SpecValueKind.Object, "The components 'examples' field must be an object.");
                    return true;
                case "links":
                    WarnIgnoredMap(
                        section.Value,
                        "components.links",
                        "The components section 'links' is not used for generation and was ignored.");
                    return true;
                case "callbacks":
                    WarnIgnoredMap(
                        section.Value,
                        "components.callbacks",
                        "The components section 'callbacks' is not used for generation and was ignored.");
                    return true;
                default:
                    return false;
            }
        }

        // Shallow check only: the node must be an object, and an empty map ignores nothing, so it is silent.
        private void WarnIgnoredMap(SpecNode node, string description, string message)
        {
            RequireKind(node, SpecValueKind.Object, "The '" + description + "' field must be an object.");
            foreach (SpecProperty _ in node.EnumerateObject())
            {
                AddIgnoredWarning(node, message);
                return;
            }
        }
    }
}
