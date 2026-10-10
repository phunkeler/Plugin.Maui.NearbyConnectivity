using Android.Content;
using AndroidUri = Android.Net.Uri;
using Path = System.IO.Path;

namespace Plugin.Maui.NearbyConnectivity;

sealed partial class AndroidAdapter
{
    AndroidUri? TryCreateUri(string fileUri)
    {
        if (string.IsNullOrWhiteSpace(fileUri))
        {
            return null;
        }

        try
        {
            AndroidUri? uri;

            if (Path.IsPathRooted(fileUri))
            {
                using var file = new Java.IO.File(fileUri);
                uri = AndroidUri.FromFile(file);
            }
            else
            {
                uri = AndroidUri.Parse(fileUri);
            }

            return IsSupportedScheme(uri)
                ? uri
                : null;
        }
        catch (Exception ex)
        {
            // The caller reports only "not a valid or supported scheme", which is the wrong
            // diagnosis when the real cause was a provider SecurityException or a malformed
            // path — so the cause is logged here rather than discarded.
            NearbyLog.FileUriParseFailed(_logger, fileUri, ex);
            return null;
        }
    }

    static bool IsSupportedScheme(AndroidUri? uri)
        => uri?.Scheme is { } scheme
            && (scheme.Equals(ContentResolver.SchemeFile, StringComparison.OrdinalIgnoreCase)
                || scheme.Equals(ContentResolver.SchemeContent, StringComparison.OrdinalIgnoreCase));

    string ResolveResourceName(AndroidUri uri) =>
        ContentResolver.SchemeContent.Equals(uri.Scheme, StringComparison.OrdinalIgnoreCase)
            ? ResolveContentUriName(uri)
            : ResolveFileUriName(uri);

    string ResolveContentUriName(AndroidUri uri)
    {
        try
        {
            var (displayName, dataPath) = QueryContentColumns(uri);

            return NameWithExtension(displayName)
                ?? NameFromDataPath(dataPath)
                ?? NameFromMimeType(uri, displayName)
                ?? displayName
                ?? uri.LastPathSegment
                ?? Guid.NewGuid().ToString("N");
        }
        catch (Exception ex)
        {
            NearbyLog.ContentUriNameUnresolved(_logger, ex);
            return Guid.NewGuid().ToString("N");
        }
    }

    // Reads _display_name, and _data where the platform still populates it. Returns a null
    // dataPath on API 29 and above, where that column is not queried at all.
    static (string? DisplayName, string? DataPath) QueryContentColumns(AndroidUri uri)
    {
        var queryData = !OperatingSystem.IsAndroidVersionAtLeast(29);

        string[] projection = queryData
            ? [Android.Provider.IOpenableColumns.DisplayName, Android.Provider.MediaStore.IMediaColumns.Data]
            : [Android.Provider.IOpenableColumns.DisplayName];

        using var cursor = Platform.AppContext.ContentResolver?.Query(
            uri,
            projection,
            selection: null,
            selectionArgs: null,
            sortOrder: null);

        if (cursor is null || !cursor.MoveToFirst())
        {
            return (null, null);
        }

        var nameIndex = cursor.GetColumnIndex(Android.Provider.IOpenableColumns.DisplayName);

        var displayName = nameIndex >= 0
            ? cursor.GetString(nameIndex)
            : null;

        if (!queryData)
        {
            return (displayName, null);
        }

        var dataIndex = cursor.GetColumnIndex(Android.Provider.MediaStore.IMediaColumns.Data);

        var dataPath = dataIndex >= 0
            ? cursor.GetString(dataIndex)
            : null;

        return (displayName, dataPath);
    }

    static string? NameFromDataPath(string? dataPath)
        => !string.IsNullOrEmpty(dataPath)
            && Path.GetFileName(dataPath) is { Length: > 0 } name
            ? name
            : null;

    static string? NameWithExtension(string? displayName) =>
        !string.IsNullOrWhiteSpace(displayName)
        && Path.GetExtension(displayName).Length > 0
            ? displayName
            : null;

    static string? NameFromMimeType(AndroidUri uri, string? displayName)
    {
        var mimeType = Platform.AppContext.ContentResolver?.GetType(uri);

        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return null;
        }

        var ext = Android.Webkit.MimeTypeMap.Singleton?.GetExtensionFromMimeType(mimeType);

        if (string.IsNullOrWhiteSpace(ext))
        {
            return null;
        }

        var stem = !string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileNameWithoutExtension(displayName)
            : Guid.NewGuid().ToString("N");

        return $"{stem}.{ext}";
    }

    static string ResolveFileUriName(AndroidUri uri)
    {
        if (uri?.Path is { Length: > 0 } filePath)
        {
            return Path.GetFileName(filePath) is { Length: > 0 } fileName
                ? fileName
                : filePath;
        }

        return Guid.NewGuid().ToString("N");
    }
}
