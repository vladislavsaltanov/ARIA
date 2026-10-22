namespace Aria.Audio;

using Aria.Core.Model;

public struct VoiceReport
{
    public int Tag;
    public long SourceFrame;
    public bool Active;
    public bool PauseWhenFaded;
    public bool Ended;
    public long EndSourceFrame;
    public double GainDb;
    public double FadeFrom;
    public double FadeTo;
    public double FadeProgress;
    public int FadeFrames;
    public FadeCurve FadeCurve;
    public bool StopWhenDone;
    public int Epoch;
}
