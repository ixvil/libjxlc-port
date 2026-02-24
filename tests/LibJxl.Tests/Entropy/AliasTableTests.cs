using LibJxl.Entropy;
using Xunit;

namespace LibJxl.Tests.Entropy;

public class AliasTableTests
{
    [Fact]
    public void FlatHistogram_SumsCorrectly()
    {
        var hist = AliasTable.CreateFlatHistogram(4, AnsParams.AnsTabSize);
        int sum = 0;
        foreach (int v in hist) sum += v;
        Assert.Equal(AnsParams.AnsTabSize, sum);
    }

    [Fact]
    public void FlatHistogram_DifferByAtMostOne()
    {
        var hist = AliasTable.CreateFlatHistogram(7, AnsParams.AnsTabSize);
        int min = hist.Min();
        int max = hist.Max();
        Assert.True(max - min <= 1);
    }

    [Fact]
    public void InitAliasTable_SingleSymbol()
    {
        int[] dist = [AnsParams.AnsTabSize]; // single symbol with full weight
        int logAlphaSize = 8;
        int tableSize = 1 << logAlphaSize;
        var table = new AliasTable.Entry[tableSize];

        var status = AliasTable.InitAliasTable(dist, AnsParams.AnsLogTabSize, logAlphaSize, table);
        Assert.True(status);

        // All entries should point to symbol 0
        for (int i = 0; i < tableSize; i++)
        {
            var sym = AliasTable.Lookup(table, i, logAlphaSize - AnsParams.AnsLogTabSize + logAlphaSize,
                (1 << (logAlphaSize - AnsParams.AnsLogTabSize + logAlphaSize)) - 1);
            // With the single-symbol case, the table should be set up correctly
            Assert.Equal(0, table[i].RightValue);
        }
    }

    [Fact]
    public void InitAliasTable_UniformDistribution()
    {
        // 4 symbols, each with weight ANS_TAB_SIZE/4
        int[] dist = AliasTable.CreateFlatHistogram(4, AnsParams.AnsTabSize);
        int logAlphaSize = 2; // 4 entries
        int tableSize = 1 << logAlphaSize;
        var table = new AliasTable.Entry[tableSize];

        var status = AliasTable.InitAliasTable(dist, AnsParams.AnsLogTabSize, logAlphaSize, table);
        Assert.True(status);
    }

    [Fact]
    public void InitAliasTable_EmptyDistribution()
    {
        int[] dist = [];
        int logAlphaSize = 2;
        int tableSize = 1 << logAlphaSize;
        var table = new AliasTable.Entry[tableSize];

        var status = AliasTable.InitAliasTable(dist, AnsParams.AnsLogTabSize, logAlphaSize, table);
        Assert.True(status);
    }

    [Fact]
    public void PopulationCountPrecision()
    {
        // Basic smoke test
        int result = AliasTable.GetPopulationCountPrecision(5, AnsParams.AnsLogTabSize);
        Assert.True(result >= 0);
    }
}

public class HuffmanTableTests
{
    [Fact]
    public void BuildTable_SingleSymbol()
    {
        byte[] codeLengths = [1]; // one symbol
        ushort[] count = new ushort[AnsParams.PrefixMaxBits + 1];
        count[1] = 1;

        int rootBits = 8;
        var table = new HuffmanCode[1 << rootBits];
        int size = HuffmanTable.BuildTable(table, 0, rootBits, codeLengths, 1, count);
        Assert.True(size > 0);

        // All entries should decode to symbol 0
        Assert.Equal(0, table[0].Value);
    }

    [Fact]
    public void BuildTable_TwoSymbols()
    {
        // Symbol 0: length 1, Symbol 1: length 1
        byte[] codeLengths = [1, 1];
        ushort[] count = new ushort[AnsParams.PrefixMaxBits + 1];
        count[1] = 2;

        int rootBits = 8;
        var table = new HuffmanCode[1 << rootBits];
        int size = HuffmanTable.BuildTable(table, 0, rootBits, codeLengths, 2, count);
        Assert.True(size > 0);
    }

    [Fact]
    public void BuildTable_ThreeSymbols()
    {
        // Symbol 0: length 1, Symbol 1: length 2, Symbol 2: length 2
        byte[] codeLengths = [1, 2, 2];
        ushort[] count = new ushort[AnsParams.PrefixMaxBits + 1];
        count[1] = 1;
        count[2] = 2;

        int rootBits = 8;
        var table = new HuffmanCode[1 << rootBits];
        int size = HuffmanTable.BuildTable(table, 0, rootBits, codeLengths, 3, count);
        Assert.True(size > 0);
    }
}
