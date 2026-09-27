// WHY: A usable producer must be consumed by the real Magic ViewModel commands/lifecycle.
// The managed preview contract (magicPreview=<session>) serves the edited DLL's own .data
// resource through /viewer-data — it never requires a vanilla 11/<id>.bin or a catalogue
// entry, which is what keeps custom and uncatalogued effects previewable.
// MAINT: Use real parsed documents, real session files and loopback servers. No UI dispatcher,
// reflection, forged fields, global resolver replacement, save command or visual-host claim.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Tests.ViewerHub;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class MagicDllEditorPreviewTests
{
    private static MagicDllEditor_ViewModel CreateVm(
        MagicPreviewFixture fixture, List<string>? sessions = null) => new()
    {
        PreviewSessionRootResolver = () => fixture.SessionRoot,
        PreviewHubResolver = (id, session) =>
        {
            sessions?.Add(session);
            return (Url(fixture.Server, id, session), fixture.Server);
        },
    };

    private static string Url(StudioWebServer server, int id, string session) =>
        "http://127.0.0.1:" + server.Port +
        "/noclip/index.html?magic=" + id + "&magicPreview=" + session + "#ffx/magic-studio";

    private static string ManifestRoute(string session) =>
        "/viewer-data/magic-preview/" + session + "/current.json";

    private static string SessionDirectory(MagicPreviewFixture fixture, string session) =>
        Path.Combine(fixture.SessionRoot, "magic-preview", session);

    private static string Document(MagicPreviewFixture fixture, string name = "magic_0021.dll") =>
        MagicDllTestFixture.WritePublishable(Path.Combine(fixture.Root, "source"), name);

    private static MagicFieldNode EditableField(MagicDllEditor_ViewModel vm) =>
        vm.RootNodes.SelectMany(root => root.Children)
            .SelectMany(group => group.Children)
            .SelectMany(program => program.Children)
            .SelectMany(slot => slot.Children)
            .OfType<MagicFieldNode>()
            .First(field => field.Type == MagicFieldType.F32 &&
                field.IsValueEditable && field.Offset >= 8 && field.SourceField != null);

    private static async Task<JsonDocument> GetManifest(
        MagicPreviewFixture fixture, string session)
    {
        using var response = await fixture.Client.GetAsync(ManifestRoute(session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PreviewCommand_OpenRefreshClose_PublishesSessionAndServesCurrentBytes()
    {
        await using var f = new MagicPreviewFixture();
        // Deliberately no Selected() bin: the managed session never consults vanilla data.
        string source = Document(f);
        var sessions = new List<string>();
        var vm = CreateVm(f, sessions);
        try
        {
            Assert.False(vm.PreviewMagicCommand.CanExecute(null));
            Assert.True(await vm.OpenFileAsync(source));
            Assert.True(vm.PreviewMagicCommand.CanExecute(null));
            vm.PreviewMagicCommand.Execute(null);

            string session = Assert.Single(sessions);
            Assert.Equal(Url(f.Server, 21, session), vm.PreviewUrl);
            Assert.True(vm.HasPreviewUrl);

            using var manifest = await GetManifest(f, session);
            var root = manifest.RootElement;
            Assert.Equal("ffx-pc-magic-v1", root.GetProperty("format").GetString());
            Assert.Equal(21, root.GetProperty("magicId").GetInt32());
            string workingRevision = root.GetProperty("workingRevision").GetString()!;
            string binaryUrl = root.GetProperty("binaryUrl").GetString()!;
            Assert.Equal(
                $"/viewer-data/magic-preview/{session}/{workingRevision}.bin", binaryUrl);
            string revision = root.GetProperty("revision").GetString()!;

            var file = vm.Document.ParsedFile!;
            byte[] expected = vm.Document.WorkingBytes!
                .AsSpan(file.DataSectionRawPtr, file.DataSectionSize).ToArray();
            Assert.Equal(expected, await f.Client.GetByteArrayAsync(binaryUrl));

            // The served snapshot is a copy: mutating the public clone cannot reach it.
            byte[] publicWorking = vm.Document.WorkingBytes!;
            publicWorking[file.DataSectionRawPtr] ^= 0xFF;
            Assert.Equal(expected, await f.Client.GetByteArrayAsync(binaryUrl));

            // Refresh after an edit republishes a new revision inside the SAME session.
            MagicFieldNode field = EditableField(vm);
            field.Value = "3.5";
            Assert.True(field.IsDirty);
            vm.PreviewMagicCommand.Execute(null);
            Assert.Single(sessions);
            using var refreshed = await GetManifest(f, session);
            string revision2 = refreshed.RootElement.GetProperty("revision").GetString()!;
            Assert.NotEqual(revision, revision2);
            byte[] current = vm.Document.WorkingBytes!
                .AsSpan(file.DataSectionRawPtr, file.DataSectionSize).ToArray();
            Assert.Equal(current, await f.Client.GetByteArrayAsync(
                refreshed.RootElement.GetProperty("binaryUrl").GetString()!));

            vm.ClosePreviewCommand.Execute(null);
            vm.ClosePreviewCommand.Execute(null); // idempotent
            Assert.False(vm.HasPreviewUrl);
            Assert.Equal(string.Empty, vm.PreviewUrl);
            Assert.False(Directory.Exists(SessionDirectory(f, session)));
            using var gone = await f.Client.GetAsync(ManifestRoute(session));
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
            f.AssertNoOutput();
        }
        finally { vm.ClosePreviewCommand.Execute(null); }
    }

    [Fact]
    public async Task PreviewCommand_DoesNotRequireACataloguedOrSeededVanillaBin()
    {
        await using var f = new MagicPreviewFixture();
        // ID 848 (0x350) is outside the published NoClip 11/ range — this is the exact
        // regression the session contract fixes: no vanilla bin is ever consulted.
        string source = Document(f, "magic_0864.dll");
        var sessions = new List<string>();
        var vm = CreateVm(f, sessions);
        try
        {
            Assert.True(await vm.OpenFileAsync(source));
            Assert.Equal(864, vm.Document.MagicId);
            vm.PreviewMagicCommand.Execute(null);
            string session = Assert.Single(sessions);
            Assert.Equal(Url(f.Server, 864, session), vm.PreviewUrl);
            using var manifest = await GetManifest(f, session);
            Assert.Equal(864, manifest.RootElement.GetProperty("magicId").GetInt32());
            Assert.Empty(Directory.GetFiles(f.MagicDirectory));
        }
        finally { vm.ClosePreviewCommand.Execute(null); }
    }

    [Fact]
    public async Task SuccessfulDocumentSwitch_ClosesOldPreviewAndUsesNewDocument()
    {
        await using var f = new MagicPreviewFixture();
        string first = Document(f);
        string second = Document(f, "magic_0042.dll");
        var sessions = new List<string>();
        var vm = CreateVm(f, sessions);
        try
        {
            Assert.True(await vm.OpenFileAsync(first));
            vm.PreviewMagicCommand.Execute(null);
            string firstSession = Assert.Single(sessions);
            Assert.True(await vm.OpenFileAsync(second));
            Assert.Equal(42, vm.Document.MagicId);
            Assert.False(vm.HasPreviewUrl);
            Assert.False(Directory.Exists(SessionDirectory(f, firstSession)));

            vm.PreviewMagicCommand.Execute(null);
            Assert.Equal(2, sessions.Count);
            string secondSession = sessions[1];
            Assert.NotEqual(firstSession, secondSession);
            Assert.Equal(Url(f.Server, 42, secondSession), vm.PreviewUrl);
            using var manifest = await GetManifest(f, secondSession);
            Assert.Equal(42, manifest.RootElement.GetProperty("magicId").GetInt32());
        }
        finally { vm.ClosePreviewCommand.Execute(null); }
    }

    [Fact]
    public async Task ServerSwitch_RefreshReleasesFirstAndPublishesSecond()
    {
        await using var a = new MagicPreviewFixture();
        await using var b = new MagicPreviewFixture();
        string source = Document(a);
        var aSessions = new List<string>();
        var vm = CreateVm(a, aSessions);
        try
        {
            Assert.True(await vm.OpenFileAsync(source));
            vm.PreviewMagicCommand.Execute(null);
            string firstSession = Assert.Single(aSessions);
            Assert.True(Directory.Exists(SessionDirectory(a, firstSession)));

            var bSessions = new List<string>();
            vm.PreviewSessionRootResolver = () => b.SessionRoot;
            vm.PreviewHubResolver = (id, session) =>
            {
                bSessions.Add(session);
                return (Url(b.Server, id, session), b.Server);
            };
            vm.PreviewMagicCommand.Execute(null);

            string secondSession = Assert.Single(bSessions);
            Assert.NotEqual(firstSession, secondSession);
            Assert.Equal(Url(b.Server, 21, secondSession), vm.PreviewUrl);
            Assert.False(Directory.Exists(SessionDirectory(a, firstSession)));
            using var manifest = await GetManifest(b, secondSession);
            Assert.Equal(21, manifest.RootElement.GetProperty("magicId").GetInt32());

            vm.ClosePreviewCommand.Execute(null);
            Assert.False(Directory.Exists(SessionDirectory(b, secondSession)));
            a.AssertNoOutput();
            b.AssertNoOutput();
        }
        finally { vm.ClosePreviewCommand.Execute(null); }
    }

    [Theory]
    [InlineData("missing-server")]
    [InlineData("missing-url")]
    [InlineData("stopped")]
    public async Task UnavailableHub_RealCommandLeavesNoSession(string kind)
    {
        await using var f = new MagicPreviewFixture();
        var vm = CreateVm(f);
        if (kind == "stopped") f.Server.Stop();
        bool resolved = false;
        vm.PreviewHubResolver = (id, session) =>
        {
            resolved = true;
            return (kind == "missing-url" ? null : Url(f.Server, id, session),
                kind == "missing-server" ? null : f.Server);
        };
        try
        {
            Assert.True(await vm.OpenFileAsync(Document(f)));
            vm.PreviewMagicCommand.Execute(null);
            Assert.True(resolved);
            Assert.False(vm.HasPreviewUrl);
            Assert.Equal(string.Empty, vm.PreviewUrl);
            Assert.Contains(vm.Log, entry =>
                entry.Level == MagicLogLevel.Error &&
                entry.Text.StartsWith(
                    Strings.U_Md_WorkingPreviewFailed.Split('{')[0], StringComparison.Ordinal));
            Assert.False(Directory.Exists(Path.Combine(f.SessionRoot, "magic-preview")) &&
                Directory.EnumerateDirectories(Path.Combine(f.SessionRoot, "magic-preview")).Any());
            if (kind == "stopped") Assert.True(f.Server.Start(0), f.Server.Status);
            f.AssertNoOutput();
        }
        finally { vm.ClosePreviewCommand.Execute(null); }
    }

    [Theory]
    [InlineData("no-root")]
    [InlineData("link-root")]
    [InlineData("unpublishable")]
    public async Task UnavailableSession_RealCommandDoesNotPublish(string kind)
    {
        await using var f = new MagicPreviewFixture();
        var vm = CreateVm(f);
        string? link = null;
        if (kind == "no-root") vm.PreviewSessionRootResolver = () => null;
        if (kind == "link-root" && OperatingSystem.IsLinux())
        {
            link = Path.Combine(f.Root, "session-alias");
            string outside = Path.Combine(f.Root, "outside");
            Directory.CreateDirectory(outside);
            Directory.CreateSymbolicLink(link, outside);
            vm.PreviewSessionRootResolver = () => link;
        }
        else if (kind == "link-root") return; // symlink creation needs privilege off Linux
        try
        {
            string source = kind == "unpublishable"
                ? MagicDllTestFixture.Write(Path.Combine(f.Root, "source"), "magic_0021.dll")
                : Document(f);
            Assert.True(await vm.OpenFileAsync(source));
            vm.PreviewMagicCommand.Execute(null);
            Assert.False(vm.HasPreviewUrl);
            Assert.Contains(vm.Log, entry =>
                entry.Level is MagicLogLevel.Warn or MagicLogLevel.Error);
            Assert.False(Directory.Exists(Path.Combine(f.SessionRoot, "magic-preview")) &&
                Directory.EnumerateDirectories(Path.Combine(f.SessionRoot, "magic-preview")).Any());
            f.AssertNoOutput();
        }
        finally
        {
            vm.ClosePreviewCommand.Execute(null);
            if (link != null) Directory.Delete(link);
        }
    }

    [Fact]
    public async Task FailedReopen_RemovesBothSessions()
    {
        await using var a = new MagicPreviewFixture();
        await using var b = new MagicPreviewFixture();
        var vm = CreateVm(a);
        try
        {
            Assert.True(await vm.OpenFileAsync(Document(a)));
            vm.PreviewMagicCommand.Execute(null);
            Assert.True(vm.HasPreviewUrl);

            // A root change forces a fresh session; when the new hub refuses, neither
            // the old nor the half-created session may survive.
            vm.PreviewSessionRootResolver = () => b.SessionRoot;
            vm.PreviewHubResolver = (_, _) => (null, b.Server);
            vm.PreviewMagicCommand.Execute(null);
            Assert.False(vm.HasPreviewUrl);
            foreach (string root in new[] { a.SessionRoot, b.SessionRoot })
                Assert.False(Directory.Exists(Path.Combine(root, "magic-preview")) &&
                    Directory.EnumerateDirectories(Path.Combine(root, "magic-preview")).Any());
            a.AssertNoOutput();
            b.AssertNoOutput();
        }
        finally { vm.ClosePreviewCommand.Execute(null); }
    }
}
