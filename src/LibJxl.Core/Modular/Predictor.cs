// Port of lib/jxl/modular/options.h — Predictor enum and utilities
// and lib/jxl/modular/encoding/context_predict.h — prediction functions
using System.Runtime.CompilerServices;

namespace LibJxl.Modular;

/// <summary>Predictor types for modular coding. Port of jxl::Predictor.</summary>
public enum Predictor
{
    Zero = 0,
    Left = 1,
    Top = 2,
    Average0 = 3,
    Select = 4,
    Gradient = 5,
    Weighted = 6,
    TopRight = 7,
    TopLeft = 8,
    LeftLeft = 9,
    Average1 = 10,
    Average2 = 11,
    Average3 = 12,
    Average4 = 13,
    NumPredictors = 14,
}

/// <summary>Property value type (int32).</summary>
public static class PropertyConstants
{
    public const int NumStaticProperties = 2; // [channel, group_id]
    public const int NumNonrefProperties = 16;
    public const int ExtraPropsPerChannel = 4;
}

/// <summary>
/// Prediction functions for modular coding.
/// Port of PredictOne, ClampedGradient from context_predict.h.
/// </summary>
public static class PredictionFunctions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ClampedGradient(int top, int left, int topleft)
    {
        int grad = top + left - topleft;
        int mn = Math.Min(top, left);
        int mx = Math.Max(top, left);
        return Math.Clamp(grad, mn < topleft ? mn : mx,
                          mn < topleft ? mx : mn);
    }

    /// <summary>Computes a prediction for a single pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long PredictOne(Predictor p, int left, int top, int toptop,
                                   int topleft, int topright, int leftleft,
                                   long wpPred)
    {
        return p switch
        {
            Predictor.Zero => 0,
            Predictor.Left => left,
            Predictor.Top => top,
            Predictor.Average0 => ((long)left + top) / 2,
            Predictor.Select => Select(left, top, topleft),
            Predictor.Gradient => ClampedGradient(top, left, topleft),
            Predictor.Weighted => wpPred,
            Predictor.TopRight => topright,
            Predictor.TopLeft => topleft,
            Predictor.LeftLeft => leftleft,
            Predictor.Average1 => ((long)left + topleft) / 2,
            Predictor.Average2 => ((long)topleft + top) / 2,
            Predictor.Average3 => ((long)top + topright) / 2,
            Predictor.Average4 => ((long)left * 6 + top * 10 + topright * 2 + topleft * 2 + 10) / 20,
            _ => 0,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Select(int left, int top, int topleft)
    {
        int p = left + top - topleft;
        int dLeft = Math.Abs(p - left);
        int dTop = Math.Abs(p - top);
        return dLeft < dTop ? left : top;
    }
}
