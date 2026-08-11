namespace NexusSyncServer.Hosting.Modules;

/// <summary>
/// The modules this instance was built with.
/// <para>Registered as a singleton so anything can report the composition — the startup log,
/// the admin view, a diagnostics endpoint. Because modules are compiled in rather than loaded
/// at runtime, "which modules is this server running" is otherwise only answerable by knowing
/// which image was built, which is exactly the question an operator cannot answer at 3am.</para>
/// </summary>
public sealed class ModuleCatalog
{
    /// <summary>Creates the catalogue from the registered modules, in registration order.</summary>
    public ModuleCatalog(IReadOnlyList<string> moduleIds) => ModuleIds = moduleIds;

    /// <summary>Module ids, in the order they were registered.</summary>
    public IReadOnlyList<string> ModuleIds { get; }

    /// <inheritdoc />
    public override string ToString() => string.Join(", ", ModuleIds);
}
