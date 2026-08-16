using ArcaDownloader.Core.Settings;
using Xunit;

namespace ArcaDownloader.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public async Task Saves_and_loads_app_settings_with_all_properties()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arca-settings-{Guid.NewGuid():N}.ini");
        try
        {
            var store = new AppSettingsStore(path);
            var initial = new AppSettings(
                OutputDirectory: @"C:\custom\download\folder",
                DownloadOriginal: false,
                CleanupTempOnSuccess: true);

            await store.SaveAsync(initial);
            var loaded = await store.LoadAsync();

            Assert.Equal(@"C:\custom\download\folder", loaded.OutputDirectory);
            Assert.False(loaded.DownloadOriginal);
            Assert.True(loaded.CleanupTempOnSuccess);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Loads_default_settings_when_file_not_found()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arca-settings-notfound-{Guid.NewGuid():N}.ini");
        var store = new AppSettingsStore(path);

        var loaded = await store.LoadAsync();

        Assert.Null(loaded.OutputDirectory);
        Assert.True(loaded.DownloadOriginal);
        Assert.False(loaded.CleanupTempOnSuccess);
    }

    [Fact]
    public async Task Handles_partial_and_commented_ini_files()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arca-settings-partial-{Guid.NewGuid():N}.ini");
        try
        {
            var content = """
                # Custom configuration
                ; Comment line
                [General]
                OutputDirectory=D:\Downloads
                """;
            await File.WriteAllTextAsync(path, content);

            var store = new AppSettingsStore(path);
            var loaded = await store.LoadAsync();

            Assert.Equal(@"D:\Downloads", loaded.OutputDirectory);
            Assert.True(loaded.DownloadOriginal);
            Assert.False(loaded.CleanupTempOnSuccess);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Handles_case_insensitive_keys_and_boolean_formats()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arca-settings-case-{Guid.NewGuid():N}.ini");
        try
        {
            var content = """
                [general]
                outputdirectory = E:\MyMedia
                downloadoriginal = false
                cleanuptemponsuccess = True
                """;
            await File.WriteAllTextAsync(path, content);

            var store = new AppSettingsStore(path);
            var loaded = await store.LoadAsync();

            Assert.Equal(@"E:\MyMedia", loaded.OutputDirectory);
            Assert.False(loaded.DownloadOriginal);
            Assert.True(loaded.CleanupTempOnSuccess);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Handles_empty_output_directory_as_null()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arca-settings-empty-{Guid.NewGuid():N}.ini");
        try
        {
            var content = """
                [General]
                OutputDirectory=
                DownloadOriginal=True
                CleanupTempOnSuccess=False
                """;
            await File.WriteAllTextAsync(path, content);

            var store = new AppSettingsStore(path);
            var loaded = await store.LoadAsync();

            Assert.Null(loaded.OutputDirectory);
            Assert.True(loaded.DownloadOriginal);
            Assert.False(loaded.CleanupTempOnSuccess);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
