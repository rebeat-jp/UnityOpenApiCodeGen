#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Rhycol.OpenApiCodeGen.Editor.Generation
{
    public sealed class GenerationProviderRegistry
    {
        readonly object _gate = new object();
        readonly Dictionary<GenerateProvider, IGenerationProvider> _providers =
            new Dictionary<GenerateProvider, IGenerationProvider>();

        public static GenerationProviderRegistry Shared { get; } =
            new GenerationProviderRegistry();

        public event Action? ProvidersChanged;

        public bool TryRegister(
            IGenerationProvider? provider,
            out string failureReason)
        {
            if (provider == null)
            {
                failureReason = "Generation provider must not be null.";
                return false;
            }

            GenerationProviderDescriptor descriptor = provider.Descriptor;
            if (!Enum.IsDefined(typeof(GenerateProvider), descriptor.Provider))
            {
                failureReason =
                    $"Unknown generation provider value: {(int)descriptor.Provider}.";
                return false;
            }

            lock (_gate)
            {
                if (_providers.TryGetValue(descriptor.Provider, out IGenerationProvider existing))
                {
                    failureReason =
                        $"Generation provider '{descriptor.Provider}' is already registered "
                        + $"by '{existing.GetType().FullName}'.";
                    return false;
                }

                _providers.Add(descriptor.Provider, provider);
            }

            failureReason = string.Empty;
            ProvidersChanged?.Invoke();
            return true;
        }

        public GenerationProviderResolution Resolve(GenerateProvider provider)
        {
            if (!Enum.IsDefined(typeof(GenerateProvider), provider))
            {
                return GenerationProviderResolution.Unresolved(
                    $"Unknown generation provider value: {(int)provider}.");
            }

            lock (_gate)
            {
                return _providers.TryGetValue(provider, out IGenerationProvider registered)
                    ? GenerationProviderResolution.Resolved(registered)
                    : GenerationProviderResolution.Unresolved(
                        $"Generation provider '{provider}' is not registered.");
            }
        }

        public IReadOnlyList<IGenerationProvider> GetRegisteredProviders()
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<IGenerationProvider>(
                    _providers
                        .OrderBy(pair => (int)pair.Key)
                        .Select(pair => pair.Value)
                        .ToList());
            }
        }
    }
}
