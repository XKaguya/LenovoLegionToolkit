using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Station.Core;
using LenovoLegionToolkit.Lib.Station.Logging;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Station.Core;

public sealed class ExtensionManager
{
    private readonly ExtensionContextFactory _contextFactory;
    private readonly IExtensionLogger _logger;
    private readonly List<IExtensionProvider> _providers = [];

    public ExtensionManager(ExtensionContextFactory contextFactory, IExtensionLogger logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public IReadOnlyCollection<IExtensionProvider> Providers => _providers.AsReadOnly();

    public void Load()
    {
        var pluginRoot = Path.Combine(Folders.AppData, "Plugins");
        _logger.Trace($"Starting extension discovery. BaseDirectory={Folders.AppData}");
        _logger.Trace($"Expected plugin root: {pluginRoot}");

        if (!Directory.Exists(pluginRoot))
        {
            _logger.Trace($"Plugin directory not found: {pluginRoot}");
            return;
        }

        var dlls = Directory.EnumerateFiles(pluginRoot, "*.dll", SearchOption.AllDirectories).ToArray();
        _logger.Trace($"Discovered {dlls.Length} plugin assembly file(s)");

        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        try
        {
            foreach (var dll in dlls)
            {
                _logger.Trace($"Discovered plugin candidate: {dll}");
                TryLoadAssemblyProviders(dll);
            }
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
        }

        _logger.Trace($"Extension discovery completed. Loaded provider count: {_providers.Count}");
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var requestedName = new AssemblyName(args.Name);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == requestedName.Name)
                return assembly;
        }
        return null;
    }

    public async Task StopAsync()
    {
        _logger.Trace($"Stopping extension providers. Count={_providers.Count}");

        foreach (var provider in _providers)
        {
            try
            {
                _logger.Trace($"Disposing provider: {provider.GetType().FullName}");
                await provider.DisposeAsync().ConfigureAwait(false);
                _logger.Trace($"Disposed provider successfully: {provider.GetType().FullName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to dispose provider {provider.GetType().FullName}", ex);
            }
        }
    }

    private void TryLoadAssemblyProviders(string assemblyPath)
    {
        try
        {
            _logger.Trace($"Loading extension assembly: {assemblyPath}");
            var assembly = Assembly.LoadFrom(assemblyPath);
            _logger.Trace($"Assembly loaded successfully: {assembly.FullName}");

            Type[] types;
            try
            {
                types = assembly.GetTypes();
                _logger.Trace($"Assembly type scan succeeded. Type count={types.Length}");
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loaderExceptions = ex.LoaderExceptions?
                    .Where(e => e is not null)
                    .Select(e => e!.Message)
                    .ToArray() ?? [];
                _logger.Error($"Failed to enumerate types from assembly {assemblyPath}. LoaderExceptions={string.Join(" | ", loaderExceptions)}", ex);
                return;
            }

            var providerTypes = types
                .Where(t => !t.IsAbstract && typeof(IExtensionProvider).IsAssignableFrom(t))
                .ToArray();

            _logger.Trace($"Provider type scan completed. Provider count={providerTypes.Length}");

            if (providerTypes.Length == 0)
            {
                _logger.Trace($"No IExtensionProvider implementations found in assembly: {assemblyPath}");
            }

            foreach (var providerType in providerTypes)
            {
                _logger.Trace($"Creating provider instance: {providerType.FullName}");

                if (Activator.CreateInstance(providerType) is not IExtensionProvider provider)
                {
                    _logger.Trace($"Activator returned null or incompatible instance for provider type: {providerType.FullName}");
                    continue;
                }

                _logger.Trace($"Initializing provider: {providerType.FullName}");
                provider.Initialize(_contextFactory.Create(providerType.FullName ?? providerType.Name));
                _providers.Add(provider);
                _logger.Trace($"Loaded provider successfully: {providerType.FullName}. Total loaded providers: {_providers.Count}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load extension assembly {assemblyPath}", ex);
        }
    }
}
