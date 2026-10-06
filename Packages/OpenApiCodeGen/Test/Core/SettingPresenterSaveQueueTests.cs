#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine.TestTools;

using Rhycol.OpenApiCodeGen.Presenter;
using Rhycol.OpenApiCodeGen.UI;

namespace Rhycol.OpenApiCodeGen.Test.Core
{
    internal sealed class SettingPresenterSaveQueueTests
    {
        [UnityTest]
        public IEnumerator OverlappingProjectSavesPersistTheLatestSnapshotLast()
        {
            var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var saved = new List<string>();
            int activeSaves = 0;
            int maximumActiveSaves = 0;
            async Task Save(ProjectSettingDisplayDto setting)
            {
                activeSaves++;
                maximumActiveSaves = Math.Max(maximumActiveSaves, activeSaves);
                if (setting.ApiDocumentFilePathOrUrl == "first")
                {
                    await firstRelease.Task;
                }

                saved.Add(setting.ApiDocumentFilePathOrUrl);
                activeSaves--;
            }

            var presenter = new SettingPresenter(Save, _ => Assert.Fail("A successful save must not report an error."));
            Task first = presenter.SaveProjectSettingAsync(new ProjectSettingDisplayDto(apiDocumentFilePathOrUrl: "first"));
            Task second = presenter.SaveProjectSettingAsync(new ProjectSettingDisplayDto(apiDocumentFilePathOrUrl: "second"));

            Assert.That(activeSaves, Is.EqualTo(1));
            Assert.That(saved, Is.Empty);
            firstRelease.SetResult(true);
            yield return AwaitWithTimeout(Task.WhenAll(first, second));

            Assert.That(saved, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(maximumActiveSaves, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FailedProjectSaveIsReportedAndDoesNotBlockTheNextSnapshot()
        {
            var firstRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failure = new InvalidOperationException("first save failed");
            var reported = new List<Exception>();
            var saved = new List<string>();
            async Task Save(ProjectSettingDisplayDto setting)
            {
                if (setting.ApiDocumentFilePathOrUrl == "first")
                {
                    await firstRelease.Task;
                    throw failure;
                }

                saved.Add(setting.ApiDocumentFilePathOrUrl);
            }

            var presenter = new SettingPresenter(Save, reported.Add);
            Task first = presenter.SaveProjectSettingAsync(new ProjectSettingDisplayDto(apiDocumentFilePathOrUrl: "first"));
            Task second = presenter.SaveProjectSettingAsync(new ProjectSettingDisplayDto(apiDocumentFilePathOrUrl: "second"));

            Assert.That(saved, Is.Empty);
            firstRelease.SetResult(true);
            yield return AwaitWithTimeout(Task.WhenAll(first, second));

            Assert.That(reported, Is.EqualTo(new[] { failure }));
            Assert.That(saved, Is.EqualTo(new[] { "second" }));
        }

        static IEnumerator AwaitWithTimeout(Task task)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!task.IsCompleted && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Project setting saves did not complete in time.");
            task.GetAwaiter().GetResult();
        }
    }
}
