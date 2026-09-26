namespace Rhycol.OpenApiCodeGen.SourceGenerator.Editor
{
    internal static class YamlDiagnosticCodes
    {
        internal static string ToCode(YamlDiagnosticCode code)
        {
            switch (code)
            {
                case YamlDiagnosticCode.LexicalError: return "YAML001";
                case YamlDiagnosticCode.InvalidIndentation: return "YAML002";
                case YamlDiagnosticCode.InvalidDocument: return "YAML003";
                case YamlDiagnosticCode.InvalidMapping: return "YAML004";
                case YamlDiagnosticCode.InvalidSequence: return "YAML005";
                case YamlDiagnosticCode.InvalidScalar: return "YAML006";
                case YamlDiagnosticCode.DuplicateKey: return "YAML007";
                case YamlDiagnosticCode.UnsupportedTag: return "YAML008";
                case YamlDiagnosticCode.UnsupportedDirective: return "YAML009";
                case YamlDiagnosticCode.MultipleDocuments: return "YAML010";
                case YamlDiagnosticCode.ComplexKey: return "YAML011";
                case YamlDiagnosticCode.UndefinedAlias: return "YAML012";
                case YamlDiagnosticCode.AliasCycle: return "YAML013";
                case YamlDiagnosticCode.AnchorRedefinition: return "YAML014";
                case YamlDiagnosticCode.LimitExceeded: return "YAML015";
                default: return "YAML000";
            }
        }
    }
}
