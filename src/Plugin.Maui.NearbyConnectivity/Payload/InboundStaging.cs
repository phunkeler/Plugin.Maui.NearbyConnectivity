namespace Plugin.Maui.NearbyConnectivity;

// Where inbound files land on the way in, for both adapters. A staged file's path depends only on
// the directory and the remote-supplied name. Only DeletePartialDestination and Sweep need an
// instance, because they log.
sealed class InboundStaging(ILogger logger)
{
    // One root for every run, so the sweep at construction collects what an earlier run left: a
    // phone app's process usually ends without disposing the session. One folder, one live session
    // per process: construction and disposal both sweep all of it.
    internal static string Root => Path.Combine(FileSystem.CacheDirectory, "nearby-received");

    // The sender chooses the name, so the count of probes for one claim is bounded. Do not count
    // up without a limit: every file a peer sends under one name would cost one more probe than
    // the last.
    internal const int MaxNumberedNames = 32;

    // Claims the path by CREATING it, so the name is reserved rather than merely observed free.
    // FileMode.CreateNew is what makes the reservation atomic: it throws if the name already
    // exists, and the loop re-resolves. Two claims can genuinely be in flight in the same directory
    // at once — Android serialises copies within an endpoint but not across them, and iOS delegate
    // callbacks for different peers arrive on different threads. Do not weaken this into a
    // check-then-write against ResolveUniquePath alone.
    //
    // The caller owns the returned stream. A caller that moves a file onto the path disposes the
    // stream first, then moves with overwrite: true — the claim stays held across the move.
    internal static FileStream ClaimUniquePath(string directory, string fileName)
    {
        // The name arrives from the remote peer, so strip any directory component it carries.
        fileName = Path.GetFileName(fileName);
        Directory.CreateDirectory(directory);

        while (true)
        {
            var candidate = ResolveUniquePath(directory, fileName);

            try
            {
                return new FileStream(candidate, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // A concurrent transfer claimed this name first. Its file now exists, so the next
                // resolve skips past it and the loop makes progress.
            }
        }
    }

    // Two peers sending photo.jpg must both be delivered, so the second is renamed rather than
    // overwriting the first. On its own this is a check, NOT a reservation — callers reach it
    // through ClaimUniquePath, which creates the file to claim the name and retries when a
    // concurrent transfer wins the race.
    internal static string ResolveUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var i = 1; i <= MaxNumberedNames; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    // The name was reserved by creating the file, so an abandoned copy leaves a zero-length or
    // partial file behind. Left in place it would both look like a delivered payload and make
    // ClaimUniquePath skip that name forever after.
    internal void DeletePartialDestination(string? destinationPath)
    {
        if (destinationPath is null)
        {
            return;
        }

        try
        {
            File.Delete(destinationPath);
        }
        catch (Exception ex)
        {
            NearbyLog.StagedFileDeleteFailed(logger, destinationPath, ex);
        }
    }

    // At disposal the work queue drains before this runs, so never sweep ahead of the drain. A copy
    // still writing when the drain times out is the residual case, so deletes stay best-effort.
    internal void Sweep(string directory)
    {
        string[] files;

        try
        {
            // One file at a time rather than the directory, so one locked file does not strand
            // the rest.
            files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (Exception ex)
        {
            NearbyLog.StagedFileDeleteFailed(logger, directory, ex);
            return;
        }

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                NearbyLog.StagedFileDeleteFailed(logger, file, ex);
            }
        }

        string[] subdirectories;

        try
        {
            subdirectories = Directory.GetDirectories(directory);
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (Exception ex)
        {
            NearbyLog.StagedFileDeleteFailed(logger, directory, ex);
            return;
        }

        foreach (var subdirectory in subdirectories)
        {
            try
            {
                Directory.Delete(subdirectory, recursive: false);
            }
            catch (Exception ex)
            {
                NearbyLog.StagedFileDeleteFailed(logger, subdirectory, ex);
            }
        }
    }
}
