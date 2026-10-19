using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Eluna.App.Services;

public sealed record VoiceChoice(string Id, string Name);

// Windows SAPI automation. All COM objects belong to a dedicated STA with a message pump.
// No System.Speech package, online speech endpoint, or audio recording is used.
internal sealed class WindowsVoiceAudio : IAsyncDisposable
{
    private static readonly Guid RecoEvents = new("7B8FCB42-0E9D-4F00-A048-7B04D6179D3D");
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<int, object, int, object> _recognition;
    private object? _recognizer, _context, _grammar, _voice, _inputStream;
    private DispatcherTimer? _timer;
    private bool _speaking, _disposed;
    private readonly string? _waveInput;
    private string? _defaultVoice;
    internal event Action<string>? Recognized;
    internal event Action<bool>? SpeakingChanged;
    internal event Action<string>? Failed;
    internal WindowsVoiceAudio(string? waveInput = null)
    {
        _waveInput = waveInput;
        _recognition = (_, _, _, result) =>
        {
            try { string text = ((dynamic)result).PhraseInfo.GetText(0, -1, true); if (!_disposed && !_speaking && !string.IsNullOrWhiteSpace(text)) { Recognized?.Invoke(text); } }
            catch (COMException) { }
        };
        var thread = new Thread(() => { _ready.SetResult(Dispatcher.CurrentDispatcher); Dispatcher.Run(); }) { IsBackground = true, Name = "Eluna voice STA" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    private static object Create(string name) => Activator.CreateInstance(Type.GetTypeFromProgID(name, throwOnError: true)!)!;
    private async Task<T> Invoke<T>(Func<T> action)
    {
        if (_disposed) { throw new ObjectDisposedException(nameof(WindowsVoiceAudio)); }
        Dispatcher dispatcher = await _ready.Task;
        return await dispatcher.InvokeAsync(action);
    }
    internal static Task<IReadOnlyList<VoiceChoice>> VoicesAsync()
    {
        var result = new TaskCompletionSource<IReadOnlyList<VoiceChoice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? voice = null, tokens = null;
            try
            {
                voice = Create("SAPI.SpVoice"); tokens = ((dynamic)voice).GetVoices(); var choices = new List<VoiceChoice>();
                for (int i = 0; i < ((dynamic)tokens).Count; i++)
                {
                    object token = ((dynamic)tokens).Item(i);
                    try { choices.Add(new((string)((dynamic)token).Id, (string)((dynamic)token).GetDescription(0))); } finally { Release(token); }
                }
                result.SetResult(choices);
            }
            catch (Exception ex) { result.SetException(ex); }
            finally { Release(tokens); Release(voice); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
    internal Task<string> ListenAsync(string? waveFile = null) => Invoke(() =>
    {
        waveFile ??= _waveInput;
        StopRecognition();
        try
        {
            _recognizer = Create("SAPI.SpInprocRecognizer");
            if (waveFile is not null)
            {
                _inputStream = Create("SAPI.SpFileStream"); ((dynamic)_inputStream).Open(waveFile, 0, false); ((dynamic)_recognizer).AudioInputStream = _inputStream;
            }
            else
            {
                object inputs = ((dynamic)_recognizer).GetAudioInputs("", "");
                try
                {
                    if (((dynamic)inputs).Count == 0) { throw new InvalidOperationException("マイクが見つかりません。Windowsのマイク接続とアプリのアクセス許可を確認してください。"); }
                    object input = ((dynamic)inputs).Item(0);
                    try { ((dynamic)_recognizer).AudioInput = input; } finally { Release(input); }
                }
                finally { Release(inputs); }
            }
            object engines = ((dynamic)_recognizer).GetRecognizers("", ""); string name = "";
            try
            {
                if (((dynamic)engines).Count == 0) { throw new InvalidOperationException("Windowsの音声認識エンジンがありません。テキストと画面共有はそのまま使えます。"); }
                int selected = 0;
                for (int i = 0; i < ((dynamic)engines).Count; i++)
                {
                    object candidate = ((dynamic)engines).Item(i);
                    try
                    {
                        string languages = ((dynamic)candidate).GetAttribute("Language");
                        if (languages.Split(';').Any(l => int.TryParse(l, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id) && id == CultureInfo.CurrentUICulture.LCID)) { selected = i; break; }
                    }
                    finally { Release(candidate); }
                }
                object engine = ((dynamic)engines).Item(selected);
                try { ((dynamic)_recognizer).Recognizer = engine; name = ((dynamic)engine).GetDescription(0); } finally { Release(engine); }
            }
            finally { Release(engines); }
            _context = ((dynamic)_recognizer).CreateRecoContext();
            ((dynamic)_context).EventInterests = 16; // SRERecognition
            ComEventsHelper.Combine(_context, RecoEvents, 7, _recognition);
            _grammar = ((dynamic)_context).CreateGrammar(1);
            ((dynamic)_grammar).DictationLoad("", 0); ((dynamic)_grammar).DictationSetState(1);
            ((dynamic)_recognizer).State = _speaking ? 0 : 1;
            return name;
        }
        catch { StopRecognition(); throw; }
    });
    internal Task MuteAsync() => Invoke(() => { StopRecognition(); return true; });
    // Integration verification uses a generated WAV instead of speaking into the user's room.
    internal Task SynthesizeFileAsync(string text, string path) => Invoke(() =>
    {
        object voice = Create("SAPI.SpVoice"), stream = Create("SAPI.SpFileStream");
        try { ((dynamic)stream).Open(path, 3, false); ((dynamic)voice).AudioOutputStream = stream; ((dynamic)voice).Speak(text, 16); ((dynamic)stream).Close(); return true; }
        finally { Release(stream); Release(voice); }
    });
    internal Task SpeakAsync(string text, string? voiceId, int rate) => Invoke(() =>
    {
        if (_voice is null)
        {
            _voice = Create("SAPI.SpVoice"); object original = ((dynamic)_voice).Voice;
            try { _defaultVoice = ((dynamic)original).Id; } finally { Release(original); }
        }
        object tokens = ((dynamic)_voice).GetVoices();
        try
        {
            bool found = false;
            for (int i = 0; i < ((dynamic)tokens).Count; i++)
            {
                object token = ((dynamic)tokens).Item(i);
                try { if ((string)((dynamic)token).Id == (voiceId ?? _defaultVoice)) { ((dynamic)_voice).Voice = token; found = true; break; } }
                finally { Release(token); }
            }
            if (!found) { throw new InvalidOperationException("プロフィールに指定された音声がありません。使用する音声を選び直してください。"); }
        }
        finally { Release(tokens); }
        ((dynamic)_voice).Rate = Math.Clamp(rate, -10, 10);
        if (_recognizer is not null) { ((dynamic)_recognizer).State = 0; } // Avoid transcribing loudspeaker output as user input.
        _speaking = true; SpeakingChanged?.Invoke(true);
        try { ((dynamic)_voice).Speak(text, 1 | 2 | 16); }
        catch { FinishSpeech(); throw; }
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => CheckSpeech(), Dispatcher.CurrentDispatcher);
        _timer.Start(); return true;
    });
    private void CheckSpeech()
    {
        try
        {
            if (_voice is not null) { object status = ((dynamic)_voice).Status; try { if ((int)((dynamic)status).RunningState == 1) { FinishSpeech(); } } finally { Release(status); } }
        }
        catch (Exception ex) { _timer?.Stop(); _speaking = false; SpeakingChanged?.Invoke(false); Failed?.Invoke(ex.Message); }
    }
    private void FinishSpeech()
    {
        _timer?.Stop(); bool was = _speaking; _speaking = false;
        if (_recognizer is not null) { ((dynamic)_recognizer).State = 1; }
        if (was) { SpeakingChanged?.Invoke(false); }
    }
    internal Task InterruptAsync() => Invoke(() => { if (_voice is not null) { ((dynamic)_voice).Speak("", 1 | 2 | 16); } FinishSpeech(); return true; });
    private void StopRecognition()
    {
        if (_recognizer is not null) { try { ((dynamic)_recognizer).State = 0; } catch (COMException) { } }
        if (_context is not null) { try { ComEventsHelper.Remove(_context, RecoEvents, 7, _recognition); } catch (ArgumentException) { } }
        Release(_grammar); Release(_context); Release(_recognizer); _grammar = _context = _recognizer = null;
        if (_inputStream is not null) { try { ((dynamic)_inputStream).Close(); } catch (COMException) { } Release(_inputStream); _inputStream = null; }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) { Marshal.FinalReleaseComObject(value); } }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        try
        {
            await Invoke(() =>
        {
            _timer?.Stop(); StopRecognition(); if (_voice is not null) { ((dynamic)_voice).Speak("", 1 | 2 | 16); }
            Release(_voice); _voice = null; return true;
        });
        }
        finally { _disposed = true; (await _ready.Task).BeginInvokeShutdown(DispatcherPriority.Background); }
    }
}
