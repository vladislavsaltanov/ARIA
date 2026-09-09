namespace Aria.Audio;

using Aria.Core.Model;

public static class FadeCurves
{
    public static double Evaluate(FadeCurve curve, double t)
    {
        if (t < 0.0)
        {
            t = 0.0;
        }
        else if (t > 1.0)
        {
            t = 1.0;
        }
        return curve switch
        {
            FadeCurve.Linear => t,
            FadeCurve.Logarithmic => Math.Sqrt(t),
            FadeCurve.Exponential => t * t,
            FadeCurve.SCurve => t * t * (3.0 - 2.0 * t),
            _ => t,
        };
    }
}
