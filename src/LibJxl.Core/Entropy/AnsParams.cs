// Port of lib/jxl/ans_params.h — ANS common parameters
namespace LibJxl.Entropy;

/// <summary>Common parameters for ANS entropy coding.</summary>
public static class AnsParams
{
    public const int AnsLogTabSize = 12;
    public const int AnsTabSize = 1 << AnsLogTabSize;  // 4096
    public const int AnsTabMask = AnsTabSize - 1;

    public const int PrefixMaxAlphabetSize = 4096;
    public const int AnsMaxAlphabetSize = 256;

    public const int PrefixMaxBits = 15;

    public const int AnsSignature = 0x13;
}
