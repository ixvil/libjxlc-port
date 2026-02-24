// Port of lib/jxl/modular/encoding/context_predict.h — weighted predictor
using LibJxl.Base;
using LibJxl.Bitstream;
using LibJxl.Entropy;

namespace LibJxl.Modular;

/// <summary>
/// Header for the weighted predictor coefficients.
/// Port of jxl::weighted::Header.
/// </summary>
public class WeightedHeader
{
    public int P1C = 16, P2C = 10;
    public int P3Ca = 7, P3Cb = 7, P3Cc = 7, P3Cd = 0, P3Ce = 0;
    public int[] W = [0xD, 0xC, 0xC, 0xC]; // 4 predictor weights

    public bool AllDefault = true;

    public JxlStatus ReadFromBitStream(BitReader br)
    {
        AllDefault = FieldReader.ReadBool(br);
        if (AllDefault) return true;

        P1C = (int)br.ReadBits(4);
        P2C = (int)br.ReadBits(4);
        P3Ca = (int)br.ReadBits(4);
        P3Cb = (int)br.ReadBits(4);
        P3Cc = (int)br.ReadBits(4);
        P3Cd = (int)br.ReadBits(4);
        P3Ce = (int)br.ReadBits(4);
        W[0] = (int)br.ReadBits(4);
        W[1] = (int)br.ReadBits(4);
        W[2] = (int)br.ReadBits(4);
        W[3] = (int)br.ReadBits(4);
        return true;
    }
}

/// <summary>
/// Adaptive weighted predictor state (per-row).
/// Port of jxl::weighted::State.
/// </summary>
public class WeightedState
{
    private readonly WeightedHeader _header;
    private readonly int _width;

    // 4 predictors: N-based, W-based, NW-based, gradient-hybrid
    private readonly long[] _prediction = new long[4];
    private readonly int[][] _predErrors; // [4][width]
    private readonly int[] _error; // width

    public WeightedState(WeightedHeader header, int width)
    {
        _header = header;
        _width = width;
        _predErrors = new int[4][];
        for (int i = 0; i < 4; i++)
            _predErrors[i] = new int[width];
        _error = new int[width];
    }

    /// <summary>
    /// Computes the weighted prediction for pixel at (x, y).
    /// Returns the weighted average of 4 base predictors.
    /// </summary>
    public long Predict(int x, int y, int xsize,
                        int n, int w, int ne, int nw, int nn)
    {
        if (y == 0 && x == 0)
        {
            _prediction[0] = 0;
            _prediction[1] = 0;
            _prediction[2] = 0;
            _prediction[3] = 0;
            return 0;
        }

        // Base predictions
        long predN = n;
        long predW = w;
        long predNW = (long)n + w - nw;
        long predNE = x < xsize - 1 ? ne : n;

        // Weighted combination
        _prediction[0] = predN - ((long)_header.P1C * (n - nn) >> 5);
        _prediction[1] = predW - ((long)_header.P2C * (w - (x > 0 ? w : 0)) >> 5);
        _prediction[2] = predNW - (((long)_header.P3Ca * (nw - n)
                                  + (long)_header.P3Cb * (nw - w)
                                  + (long)_header.P3Cc * (ne - n)
                                  + (long)_header.P3Cd * (n - nn)
                                  + (long)_header.P3Ce * (ne - (y > 0 ? nn : 0))) >> 5);
        _prediction[3] = (predN + predW) / 2;

        // Compute weights based on error history
        long sumWeights = 0;
        long sumPredictions = 0;
        for (int i = 0; i < 4; i++)
        {
            long weight = (long)_header.W[i];
            int errX = x > 0 ? _predErrors[i][x - 1] : 0;
            int errUp = y > 0 ? _predErrors[i][x] : 0;
            int errTotal = errX + errUp;
            // Reduce weight based on accumulated error
            if (errTotal > 0)
            {
                int shift = Math.Min(BitOps.FloorLog2Nonzero((uint)errTotal + 1), 12);
                weight >>= shift / 2;
            }
            weight = Math.Max(weight, 1);
            sumWeights += weight;
            sumPredictions += weight * _prediction[i];
        }

        return (sumPredictions + sumWeights / 2) / sumWeights;
    }

    /// <summary>
    /// Updates the error tracking after a pixel is decoded.
    /// </summary>
    public void UpdateErrors(int x, int val, long prediction)
    {
        int err = Math.Abs(val - (int)prediction);
        _error[x] = err;

        for (int i = 0; i < 4; i++)
        {
            int predErr = Math.Abs(val - (int)_prediction[i]);
            _predErrors[i][x] = predErr;
        }
    }
}
