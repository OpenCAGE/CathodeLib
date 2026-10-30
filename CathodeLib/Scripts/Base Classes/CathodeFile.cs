using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;

namespace CathodeLib
{
    public class CathodeFile
    {
        public Action<string> OnLoadBegin;
        public Action<string> OnLoadSuccess;

        public Action<string> OnSaveBegin;
        public Action<string> OnSaveSuccess;

        public string Filepath { get { return _filepath; } }
        protected string _filepath = "";

        public bool Loaded { get { return _loaded; } }
        protected bool _loaded = false;

        public static Implementation Implementation = Implementation.NONE;

        /// <summary>
        /// Override this property to return true if the derived class handles loading manually in its constructor
        /// </summary>
        protected virtual bool HandlesLoadingManually => false;

        public CathodeFile(string filepath)
        {
            _filepath = filepath;

            if (!HandlesLoadingManually)
                _loaded = Load();
        }

        public CathodeFile(MemoryStream stream, string virtualPath = "")
        {
            _filepath = virtualPath;
            if (!HandlesLoadingManually)
                _loaded = Load(stream);
        }

        public CathodeFile(byte[] data, string virtualPath = "")
        {
            _filepath = virtualPath;
            if (!HandlesLoadingManually)
            {
                using (var stream = new MemoryStream(data))
                {
                    _loaded = Load(stream);
                }
            }
        }

        #region EXTERNAL_FUNCS
        /// <summary>
        /// Try and load the file, if it exists
        /// </summary>
        protected bool Load()
        {
            return Load(File.Exists(_filepath) ? new MemoryStream(File.ReadAllBytes(_filepath)) : null);
        }

        /// <summary>
        /// Load as <see cref="Load()"/> does, but from the file where it is rather than a copy of all of it in memory: for a format
        /// whose file runs to hundreds of MB and is only read through once (each entry copied out), so it is never in memory twice.
        /// It is opened as Load opens it to read it all, so what failed to open before fails the same way - and what fails in
        /// reading the file itself (a drive gone, a bad sector) is still thrown, as it was from Load's read of all of it, rather
        /// than taken for the file failing to load. <paramref name="read"/> is given it in LoadInternal's place.
        /// </summary>
        protected bool LoadFromFile(Func<Stream, bool> read, int bufferSize)
        {
            if (!File.Exists(_filepath))
                return Load(null);

            using (FileStream stream = new FileStream(_filepath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize))
            {
                OnLoadBegin?.Invoke(_filepath);
                if (stream.Length == 0) return false;

                try
                {
#if !CATHODE_FAIL_HARD
                    try
                    {
#endif
                        if (read(new MarkedReads(stream)))
                        {
                            OnLoadSuccess?.Invoke(_filepath);
                            return true;
                        }
                        else return false;
#if !CATHODE_FAIL_HARD
                    }
                    catch (Exception e) when (!(e is FileReadFailure))
                    {
                        return false;
                    }
#endif
                }
                catch (FileReadFailure failure)
                {
                    ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
                    throw;
                }
            }
        }

        //What the file itself failed to give, as LoadFromFile's read met it (see there)
        private sealed class FileReadFailure : Exception
        {
            public FileReadFailure(Exception inner) : base(inner.Message, inner) { }
        }

        //The file for LoadFromFile's read: its reads' own failures marked as such. Seeks are left as they are - one before the
        //start is the parse going wrong, and was as much a failure to load when it was a seek in a copy of the file.
        private sealed class MarkedReads : Stream
        {
            private readonly Stream _file;
            public MarkedReads(Stream file) { _file = file; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                try { return _file.Read(buffer, offset, count); }
                catch (Exception e) { throw new FileReadFailure(e); }
            }
            public override int ReadByte()
            {
                try { return _file.ReadByte(); }
                catch (Exception e) { throw new FileReadFailure(e); }
            }
#if GODOT
            //.NET's BinaryReader reads into spans: straight through, rather than Stream's copy by way of a rented array
            public override int Read(Span<byte> buffer)
            {
                try { return _file.Read(buffer); }
                catch (Exception e) { throw new FileReadFailure(e); }
            }
#endif
            public override bool CanRead => _file.CanRead;
            public override bool CanSeek => _file.CanSeek;
            public override bool CanWrite => false;
            public override long Length => _file.Length;
            public override long Position { get => _file.Position; set => _file.Position = value; }
            public override long Seek(long offset, SeekOrigin origin) => _file.Seek(offset, origin);
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>
        /// Load from MemoryStream
        /// </summary>
        protected bool Load(MemoryStream stream)
        {
            OnLoadBegin?.Invoke(_filepath);
            if (stream == null || stream.Length == 0) return false;

#if !CATHODE_FAIL_HARD
            try
            {
#endif
                if (LoadInternal(stream))
                {
                    OnLoadSuccess?.Invoke(_filepath);
                    return true;
                }
                else return false;
#if !CATHODE_FAIL_HARD
            }
            catch
            {
                return false;
            }
#endif
        }

        /// <summary>
        /// Save the file back to its original filepath.
        /// What the save writes - the file, the files its format keeps beside it, and the .META tables written to it by
        /// <see cref="OnSaveSuccess"/> (which is given the path it is being written to) - goes into a folder of its own beside
        /// the file first, and replaces what is on disk only once all of it is written: a save that fails part-way, or a file
        /// that can't be replaced (read-only, or open in another program), leaves the files on disk as they were. Inside a
        /// <see cref="BeginSaveTransaction"/> it is left there for the transaction to swap in with the rest. Where no such
        /// folder can be made the file is written in place, as it always was.
        /// False when it fails, with <see cref="LastSaveError"/> saying why when something threw.
        /// </summary>
        public bool Save()
        {
            LastSaveError = null;
            OnSaveBegin?.Invoke(_filepath);
            if (_filepath == "") return false;

            SaveTransaction transaction = _transaction.Value;
            StagedSave staged = StagedSave.Begin(_filepath);
            if (staged != null)
                _filepath = staged.StagedPath;
            bool saved = false;
            bool handedOver = false;
#if !CATHODE_FAIL_HARD
            try
            {
#endif
                try
                {
                    if (SaveInternal())
                    {
                        OnSaveSuccess?.Invoke(_filepath);
                        if (staged != null)
                        {
                            handedOver = transaction != null && transaction.Add(staged, _filepath);
                            if (!handedOver)
                                staged.Commit(_filepath);
                        }
                        saved = true;
                    }
                }
                finally
                {
                    if (staged != null)
                    {
                        _filepath = staged.PathOnDisk(_filepath);
                        if (!handedOver)
                            staged.Dispose();
                    }
                }
                if (!saved)
                    transaction?.Fail(_filepath, null);
                return saved;
#if !CATHODE_FAIL_HARD
            }
            catch (Exception e)
            {
                LastSaveError = e;
                transaction?.Fail(_filepath, e);
                return false;
            }
#endif
        }

        /// <summary>Why the last <see cref="Save()"/> failed, when something threw (null after one that worked).</summary>
        public Exception LastSaveError { get; private set; }

        /// <summary>
        /// Save the file to a new path, and optionally remember it for future saves
        /// </summary>
        public bool Save(string path = "", bool updatePath = true)
        {
            string origFilepath = updatePath && path != "" ? path : _filepath;
            if (path != "") _filepath = path;
            bool saved = Save();
            if (!updatePath) _filepath = origFilepath;
            return saved;
        }
        #endregion

        #region SAFE_SAVES
        /// <summary>
        /// Save many files as one, until the transaction is disposed. Each file saved meanwhile - on this thread, and in the
        /// work it starts (Parallel, Task.Run) - is written beside where it belongs and left there, and
        /// <see cref="SaveTransaction.Commit"/> swaps them all in together. If any of them failed to save, or one can't be
        /// swapped in, none of them is: what was on disk stays as it was. Disposing it without a commit throws away what was
        /// written. Only what goes through <see cref="Save()"/> is held back; a file written straight to disk is not.
        /// </summary>
        public static SaveTransaction BeginSaveTransaction()
        {
            SaveTransaction transaction = new SaveTransaction(_transaction.Value);
            _transaction.Value = transaction;
            return transaction;
        }
        private static readonly AsyncLocal<SaveTransaction> _transaction = new AsyncLocal<SaveTransaction>();

        /// <summary>A file that could not be saved, and why.</summary>
        public sealed class SaveFailure
        {
            public string Path { get; }
            /// <summary>What was thrown, or null when the file's format had nothing it could write.</summary>
            public Exception Error { get; }
            public string Reason => Error?.Message ?? "its data could not be written";

            internal SaveFailure(string path, Exception error)
            {
                Path = path;
                Error = error;
            }
        }

        /// <summary>Files saved as one (see <see cref="BeginSaveTransaction"/>).</summary>
        public sealed class SaveTransaction : IDisposable
        {
            private readonly SaveTransaction _outer;
            private readonly object _lock = new object();
            //Written and waiting, in the order they were saved, each with the path it was written to
            private readonly List<(StagedSave staged, string written)> _staged = new List<(StagedSave, string)>();
            private readonly List<SaveFailure> _failures = new List<SaveFailure>();
            private bool _closed = false;

            internal SaveTransaction(SaveTransaction outer)
            {
                _outer = outer;
            }

            /// <summary>The files that failed to save, or could not be swapped in.</summary>
            public IReadOnlyList<SaveFailure> Failures
            {
                get { lock (_lock) return _failures.ToArray(); }
            }

            //A file written and waiting for the commit. False once the transaction is over: the save swaps it in by itself.
            internal bool Add(StagedSave staged, string written)
            {
                lock (_lock)
                {
                    if (_closed)
                        return false;
                    _staged.Add((staged, written));
                    return true;
                }
            }

            internal void Fail(string path, Exception error)
            {
                lock (_lock)
                    _failures.Add(new SaveFailure(path, error));
            }

            /// <summary>
            /// Swap in every file written: true when all of them are on disk. False, with nothing changed on disk, when one
            /// failed to save or one can't be swapped in (that one is added to <see cref="Failures"/>).
            /// </summary>
            public bool Commit()
            {
                List<(StagedSave staged, string written)> staged;
                lock (_lock)
                {
                    if (_closed || _failures.Count != 0)
                    {
                        _closed = true;
                        return false;
                    }
                    _closed = true;
                    staged = new List<(StagedSave, string)>(_staged);
                }

                int done = 0;
                try
                {
                    for (; done < staged.Count; done++)
                        staged[done].staged.Commit(staged[done].written);
                    return true;
                }
                catch (Exception e)
                {
                    //Everything swapped in before it goes back out, newest first
                    for (int i = done - 1; i >= 0; i--)
                        staged[i].staged.RollBack();
                    Fail(staged[done].staged.FailedPath ?? staged[done].staged.PathOnDisk(staged[done].written), e);
                    return false;
                }
            }

            public void Dispose()
            {
                List<(StagedSave staged, string written)> staged;
                lock (_lock)
                {
                    _closed = true;
                    staged = new List<(StagedSave, string)>(_staged);
                    _staged.Clear();
                }
                foreach ((StagedSave save, string written) in staged)
                    save.Dispose();
                if (_transaction.Value == this)
                    _transaction.Value = _outer;
            }
        }

        /* A save written into a folder of its own beside its file, then swapped in for what is on disk (see Save) */
        internal sealed class StagedSave : IDisposable
        {
            //The folders saves are written in. One a save never finished (OpenCAGE closed part-way through) leaves behind is
            //removed by a later save in the same place.
            private const string FolderPrefix = ".cathode-save-";
            private static readonly TimeSpan Abandoned = TimeSpan.FromMinutes(10);
            private static readonly HashSet<string> _inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            //What was on disk under a file's name once it is swapped in, kept in the save's folder until the save is over
            private const string KeptSuffix = ".kept";

            private readonly string _target;    //the file's path, as given
            private readonly string _name;      //...its name
            private readonly string _directory; //...the folder it is in
            private readonly string _folder;    //where it is written first
            //.META files beside it (tables written to a file that can't carry them, a radiosity bake's marker): copied in, so
            //what the save writes to them is swapped in with the file, and one the save deletes goes from disk too
            private readonly List<string> _sidecars = new List<string>();
            //What Commit changed on disk, in order: a file replaced (kept: what was there), a new one (kept: null), or a
            //sidecar the save deleted, moved aside (removed)
            private readonly List<(string destination, string kept, bool removed)> _changed = new List<(string, string, bool)>();
            //Something on disk could not be put back: what it was is still in the folder, so the folder stays
            private bool _keepFolder = false;

            public string StagedPath => Path.Combine(_folder, _name);

            /// <summary>The file on disk that couldn't be swapped in, when Commit failed (it may be a sibling of the file).</summary>
            public string FailedPath { get; private set; }

            private StagedSave(string target, string name, string directory, string folder)
            {
                _target = target;
                _name = name;
                _directory = directory;
                _folder = folder;
            }

            /// <summary>Null when there is nowhere to write it first: the file's folder is missing, or won't take a folder.</summary>
            public static StagedSave Begin(string target)
            {
                string folder = null;
                try
                {
                    string name = Path.GetFileName(target);
                    string directory = Path.GetDirectoryName(Path.GetFullPath(target));
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                        return null;
                    RemoveAbandoned(directory);

                    folder = Path.Combine(directory, FolderPrefix + Guid.NewGuid().ToString("N").Substring(0, 12));
                    lock (_inUse)
                        _inUse.Add(folder);
                    DirectoryInfo created = Directory.CreateDirectory(folder);
                    try { created.Attributes |= FileAttributes.Hidden; } catch { }

                    StagedSave save = new StagedSave(target, name, directory, folder);
                    save.CopySidecarsIn();
                    return save;
                }
                catch
                {
                    if (folder != null)
                    {
                        TryDelete(folder);
                        lock (_inUse)
                            _inUse.Remove(folder);
                    }
                    return null;
                }
            }

            /// <summary>
            /// Where a file written here is on disk: the path as given, under the name the save left it with (a format may add
            /// or drop a compressed extension as it writes).
            /// </summary>
            public string PathOnDisk(string written)
            {
                string name = Path.GetFileName(written);
                return name == _name ? _target : _target.Substring(0, _target.Length - _name.Length) + name;
            }

            private void CopySidecarsIn()
            {
                string stem = _name;
                foreach (string compressed in new[] { ".GZ", ".FZIP" })
                {
                    if (stem.EndsWith(compressed, StringComparison.OrdinalIgnoreCase))
                    {
                        stem = stem.Substring(0, stem.Length - compressed.Length);
                        break;
                    }
                }
                foreach (string path in Directory.GetFiles(_directory, stem + "*.META"))
                {
                    string file = Path.GetFileName(path);
                    if (!file.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase) || !file.EndsWith(".META", StringComparison.OrdinalIgnoreCase))
                        continue;
                    File.Copy(path, Path.Combine(_folder, file));
                    _sidecars.Add(file);
                }
            }

            /// <summary>
            /// Swap what was written in for what is on disk, all of it or none: if one file can't be swapped in (open in another
            /// program, or read-only), what went in before it is put back and this throws. <paramref name="written"/> is the
            /// file itself as written here, which goes in last.
            /// </summary>
            public void Commit(string written)
            {
                string main = Path.GetFileName(written);
                List<string> files = new List<string>();
                foreach (string path in Directory.GetFiles(_folder))
                    files.Add(Path.GetFileName(path));
                files.Sort((a, b) => (string.Equals(a, main, StringComparison.OrdinalIgnoreCase) ? 1 : 0) - (string.Equals(b, main, StringComparison.OrdinalIgnoreCase) ? 1 : 0));
                List<string> removed = _sidecars.FindAll(sidecar => !files.Exists(file => string.Equals(file, sidecar, StringComparison.OrdinalIgnoreCase)));

                string destination = null, kept = null;
                try
                {
                    foreach (string file in files)
                    {
                        string source = Path.Combine(_folder, file);
                        destination = Path.Combine(_directory, file);
                        if (File.Exists(destination))
                        {
                            //Replaced under the name it has on disk, not the case the path asked for: the Mac, Linux and iOS builds
                            //ship their files in lower case, and the case-sensitive file systems they run on can't find a renamed one
                            destination = SpelledAsOnDisk(destination);
                            kept = source + KeptSuffix;
                            Retry(() =>
                            {
                                //A failed attempt can leave what was on disk moved aside under the kept name
                                if (!File.Exists(destination) && File.Exists(kept))
                                    File.Move(kept, destination);
                                File.Replace(source, destination, kept, true);
                            });
                            _changed.Add((destination, kept, false));
                        }
                        else
                        {
                            kept = null;
                            Retry(() => File.Move(source, destination));
                            _changed.Add((destination, null, false));
                        }
                    }
                    foreach (string file in removed)
                    {
                        destination = Path.Combine(_directory, file);
                        kept = null;
                        if (!File.Exists(destination))
                            continue;
                        string aside = Path.Combine(_folder, file + KeptSuffix);
                        Retry(() => File.Move(destination, aside));
                        _changed.Add((destination, aside, true));
                    }
                }
                catch
                {
                    FailedPath = destination;
                    if (kept != null && !File.Exists(destination) && File.Exists(kept))
                    {
                        try { File.Move(kept, destination); }
                        catch { _keepFolder = true; }
                    }
                    RollBack();
                    throw;
                }
            }

            /// <summary>Put back everything Commit changed on disk, newest first (when something after it couldn't go in).</summary>
            public void RollBack()
            {
                for (int i = _changed.Count - 1; i >= 0; i--)
                {
                    (string destination, string kept, bool removed) = _changed[i];
                    try
                    {
                        if (kept == null)
                            Retry(() => File.Delete(destination));
                        else if (removed || !File.Exists(destination))
                            Retry(() => File.Move(kept, destination));
                        else
                            Retry(() => File.Replace(kept, destination, null, true));
                    }
                    catch
                    {
                        _keepFolder = true;
                    }
                }
                _changed.Clear();
            }

            public void Dispose()
            {
                if (!_keepFolder)
                    TryDelete(_folder);
                lock (_inUse)
                    _inUse.Remove(_folder);
            }

            /// <summary>An existing file's path with its name spelled as it is on disk (the path as given if it can't be read).</summary>
            private static string SpelledAsOnDisk(string path)
            {
                try
                {
                    string name = Path.GetFileName(path);
                    foreach (string found in Directory.EnumerateFiles(Path.GetDirectoryName(path), name))
                        if (string.Equals(Path.GetFileName(found), name, StringComparison.OrdinalIgnoreCase))
                            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileName(found));
                }
                catch
                {
                }
                return path;
            }

            //A file another program has open for a moment (a virus scanner reading what was just written, say) is tried again
            //for about a second before the save gives up on it
            private static void Retry(Action action)
            {
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        action();
                        return;
                    }
                    catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < 8)
                    {
                        Thread.Sleep(125);
                    }
                }
            }

            private static void RemoveAbandoned(string directory)
            {
                try
                {
                    foreach (string folder in Directory.GetDirectories(directory, FolderPrefix + "*"))
                    {
                        lock (_inUse)
                        {
                            if (_inUse.Contains(folder))
                                continue;
                        }
                        try
                        {
                            if (DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) < Abandoned)
                                continue;
                            //One holding what a file was before a save that couldn't put it back is left for someone to recover
                            if (Directory.GetFiles(folder, "*" + KeptSuffix).Length != 0)
                                continue;
                            TryDelete(folder);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            private static void TryDelete(string folder)
            {
                try
                {
                    if (!Directory.Exists(folder))
                        return;
                    //A copy of a read-only file would stop the folder going
                    foreach (string file in Directory.GetFiles(folder))
                        try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                    Directory.Delete(folder, true);
                }
                catch { }
            }
        }
        #endregion

        #region TO_OVERRIDE
        /// <summary>
        /// Virtual function to override in inherited classes for loading the file
        /// </summary>
        protected virtual bool LoadInternal(MemoryStream stream)
        {
            Console.WriteLine("WARNING: This class does not implement loading functionality!");
            return false;
        }

        /// <summary>
        /// Virtual function to override in inherited classes for saving the file
        /// </summary>
        protected virtual bool SaveInternal()
        {
            Console.WriteLine("WARNING: This class does not implement saving functionality!");
            return false;
        }
        #endregion
    }

    [Flags]
    public enum Implementation
    {
        NONE = 1,
        CREATE = 2,
        LOAD = 4,
        SAVE = 8,
    }
}
