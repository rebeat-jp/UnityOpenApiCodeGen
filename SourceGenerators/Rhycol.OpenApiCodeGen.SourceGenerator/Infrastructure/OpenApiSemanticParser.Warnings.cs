using System;

namespace Rhycol.OpenApiCodeGen.SourceGenerator
{
    internal sealed partial class OpenApiSemanticParser
    {
        private readonly OpenApiWarningCollector _warnings = new OpenApiWarningCollector();

        // Test seam invoked at the start of ParseDocument; null in production.
        private Action<OpenApiSemanticParser, SpecNode>? _injectWarnings;

        /// <summary>
        /// 無視した要素の Warning（OACG108）を蓄積する。location は node の logical path を使う。
        /// 重複排除は (位置, メッセージ) の完全一致のみ。致命エラーで失敗した場合、
        /// 蓄積済みの warning は破棄され報告されない。
        /// Records an ignored-element warning (OACG108) at the node's logical path. De-duplication is
        /// by exact (location, message) match. If parsing fails with a fatal error, collected
        /// warnings are discarded and not reported.
        /// </summary>
        internal void AddIgnoredWarning(SpecNode node, string message)
        {
            _warnings.Add(CreateLocation(node), message);
        }
    }
}
