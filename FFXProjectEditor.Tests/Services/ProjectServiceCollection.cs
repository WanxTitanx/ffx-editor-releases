using Xunit;

namespace FFXProjectEditor.Tests.Services;

// Project_Service is a process-wide singleton (ProjectPath, GameRootOverride, persisted
// settings files). Every test class that mutates it must join this collection so xUnit
// never runs two of them in parallel and lets one test's state clobber another's asserts.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProjectServiceCollection
{
    public const string Name = "Project service singleton";
}
