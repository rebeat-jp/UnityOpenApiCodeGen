using System;
using System.IO;
using NUnit.Framework;
using Rhycol.OpenApiCodeGen.UI;
using UnityEditor.PackageManager;

namespace Rhycol.OpenApiCodeGen.Test.Core
{
    internal sealed class SupportMatrixLinkTests
    {
        [Test]
        public void InstalledBasePackageProvidesOfflineSupportMatrix()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(GenerationProviderPresentation).Assembly);
            Assert.That(package, Is.Not.Null);
            var uri = new Uri(GenerationProviderPresentation.GetSupportMatrixUrl(package.resolvedPath));
            Assert.That(uri.IsFile, Is.True);
            string html = File.ReadAllText(uri.LocalPath);
            Assert.That(html, Does.Contain("Source Generator (Beta)"));
            Assert.That(html, Does.Contain("<table>"));
            Assert.That(html, Does.Not.Contain("<script"));
        }

        [Test]
        public void SupportMatrixFileUriPreservesSpacesJapaneseAndReservedCharacters()
        {
            string root = Path.Combine(Path.GetTempPath(), "OpenApi docs 日本語 # % " + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(root, GenerationProviderPresentation.SupportMatrixRelativePath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, "<!doctype html><title>Support Matrix</title>");
                var uri = new Uri(GenerationProviderPresentation.GetSupportMatrixUrl(root));
                Assert.That(uri.IsFile, Is.True);
                Assert.That(uri.LocalPath, Is.EqualTo(file));
                Assert.That(uri.Fragment, Is.Empty);
                Assert.That(uri.Query, Is.Empty);
                Assert.That(File.ReadAllText(uri.LocalPath), Does.Contain("Support Matrix"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void MissingDocumentationDoesNotReturnAnOnlineFallback()
        {
            Assert.Throws<FileNotFoundException>(() =>
                GenerationProviderPresentation.GetSupportMatrixUrl(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        }
    }
}
