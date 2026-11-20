using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private NativeFileEditor fileEditor;

        // Windows' OpenHerFolder: the folder her notes live in, in their file manager.
        internal void OpenHerFolder()
        {
            string where = Brain.MemoryPath == null ? null : Path.GetDirectoryName(Brain.MemoryPath);
            if (string.IsNullOrEmpty(where)) { Talk.Answer("(her folder)", "I don't have a folder yet."); return; }
            try
            {
                Directory.CreateDirectory(where);
                Limbs.Launch(OperatingSystem.IsMacOS() ? "open" : "xdg-open", new[] { where });
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception || error is InvalidOperationException)
            { Talk.Answer("(her folder)", "Couldn't open my folder. It's at " + where + "."); }
        }

        internal void OpenFiles(int which)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(() => OpenFiles(which)); return; }
            if (fileEditor == null || fileEditor.Over)
            {
                var editor = new NativeFileEditor();
                fileEditor = editor;
                editor.Saved += () => mainframe?.InvalidateVisual();
                editor.Closed += (_, _) => { if (ReferenceEquals(fileEditor, editor)) fileEditor = null; };
            }
            fileEditor.Open(which);
        }

        private bool keepingFileDrafts;
        private async void FinishAfterFileDrafts(Action finish)
        {
            if (keepingFileDrafts) return;
            keepingFileDrafts = true;
            try
            {
                string error = fileEditor == null || fileEditor.Over ? null : await fileEditor.PrepareShutdownAsync();
                if (error != null) { Brain.Log("file-editor-draft", -1, error); return; }
                finish();
            }
            finally { keepingFileDrafts = false; }
        }
    }
}
