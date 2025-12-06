// ProtonOS DDK - Audio Device Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Audio;

/// <summary>
/// Audio sample format.
/// </summary>
public enum AudioFormat
{
    Unknown = 0,

    /// <summary>8-bit unsigned PCM.</summary>
    U8,

    /// <summary>16-bit signed PCM, little endian.</summary>
    S16LE,

    /// <summary>16-bit signed PCM, big endian.</summary>
    S16BE,

    /// <summary>24-bit signed PCM, little endian.</summary>
    S24LE,

    /// <summary>32-bit signed PCM, little endian.</summary>
    S32LE,

    /// <summary>32-bit floating point, little endian.</summary>
    F32LE,
}

/// <summary>
/// Audio stream direction.
/// </summary>
public enum AudioDirection
{
    /// <summary>Playback (output).</summary>
    Playback,

    /// <summary>Capture (input).</summary>
    Capture,
}

/// <summary>
/// Audio device capabilities.
/// </summary>
[Flags]
public enum AudioCapabilities
{
    None = 0,

    /// <summary>Supports playback.</summary>
    Playback = 1 << 0,

    /// <summary>Supports capture.</summary>
    Capture = 1 << 1,

    /// <summary>Supports volume control.</summary>
    Volume = 1 << 2,

    /// <summary>Supports mute control.</summary>
    Mute = 1 << 3,

    /// <summary>Supports multiple channels.</summary>
    MultiChannel = 1 << 4,

    /// <summary>Supports S/PDIF output.</summary>
    SPDIF = 1 << 5,

    /// <summary>Supports hardware mixing.</summary>
    HardwareMixing = 1 << 6,
}

/// <summary>
/// Audio stream configuration.
/// </summary>
public struct AudioConfig
{
    /// <summary>Sample rate in Hz (e.g., 44100, 48000).</summary>
    public uint SampleRate;

    /// <summary>Number of channels (1=mono, 2=stereo, etc.).</summary>
    public byte Channels;

    /// <summary>Sample format.</summary>
    public AudioFormat Format;

    /// <summary>Buffer size in frames.</summary>
    public uint BufferSize;

    /// <summary>Period size in frames (callback interval).</summary>
    public uint PeriodSize;

    /// <summary>Bytes per sample for current format.</summary>
    public int BytesPerSample => Format switch
    {
        AudioFormat.U8 => 1,
        AudioFormat.S16LE or AudioFormat.S16BE => 2,
        AudioFormat.S24LE => 3,
        AudioFormat.S32LE or AudioFormat.F32LE => 4,
        _ => 0
    };

    /// <summary>Bytes per frame (sample * channels).</summary>
    public int BytesPerFrame => BytesPerSample * Channels;
}

/// <summary>
/// Delegate for audio data callback.
/// </summary>
/// <param name="buffer">Buffer to fill (playback) or read from (capture)</param>
/// <param name="frames">Number of frames to process</param>
public unsafe delegate void AudioCallback(byte* buffer, int frames);

/// <summary>
/// Audio stream handle.
/// </summary>
public interface IAudioStream : IDisposable
{
    /// <summary>Stream configuration.</summary>
    AudioConfig Config { get; }

    /// <summary>Stream direction.</summary>
    AudioDirection Direction { get; }

    /// <summary>True if stream is running.</summary>
    bool IsRunning { get; }

    /// <summary>Start the stream.</summary>
    bool Start();

    /// <summary>Stop the stream.</summary>
    void Stop();

    /// <summary>Pause the stream.</summary>
    void Pause();

    /// <summary>Resume a paused stream.</summary>
    void Resume();

    /// <summary>
    /// Write audio data for playback.
    /// </summary>
    /// <param name="buffer">Audio data</param>
    /// <param name="frames">Number of frames to write</param>
    /// <returns>Number of frames written</returns>
    unsafe int Write(byte* buffer, int frames);

    /// <summary>
    /// Read captured audio data.
    /// </summary>
    /// <param name="buffer">Buffer to receive data</param>
    /// <param name="frames">Maximum frames to read</param>
    /// <returns>Number of frames read</returns>
    unsafe int Read(byte* buffer, int frames);

    /// <summary>
    /// Get available frames for writing/reading.
    /// </summary>
    int AvailableFrames { get; }

    /// <summary>
    /// Set the audio callback for low-latency streaming.
    /// </summary>
    void SetCallback(AudioCallback? callback);
}

/// <summary>
/// Interface for audio device drivers.
/// </summary>
public interface IAudioDevice : IDriver
{
    /// <summary>
    /// Device name for identification.
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// Device capabilities.
    /// </summary>
    AudioCapabilities Capabilities { get; }

    /// <summary>
    /// Number of playback channels.
    /// </summary>
    int PlaybackChannels { get; }

    /// <summary>
    /// Number of capture channels.
    /// </summary>
    int CaptureChannels { get; }

    /// <summary>
    /// Supported sample rates.
    /// </summary>
    uint[] SupportedSampleRates { get; }

    /// <summary>
    /// Supported sample formats.
    /// </summary>
    AudioFormat[] SupportedFormats { get; }

    /// <summary>
    /// Get/set master volume (0.0 to 1.0).
    /// </summary>
    float MasterVolume { get; set; }

    /// <summary>
    /// Get/set mute state.
    /// </summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// Open an audio stream.
    /// </summary>
    /// <param name="direction">Playback or capture</param>
    /// <param name="config">Desired configuration (may be adjusted)</param>
    /// <returns>Audio stream handle, or null on failure</returns>
    IAudioStream? OpenStream(AudioDirection direction, ref AudioConfig config);

    /// <summary>
    /// Get the default/preferred configuration.
    /// </summary>
    AudioConfig GetDefaultConfig(AudioDirection direction);
}
