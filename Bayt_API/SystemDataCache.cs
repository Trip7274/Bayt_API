namespace Bayt_API;

public class SystemDataCache
{
    public SystemDataCache()
    {
        ApiConfig.LastUpdated = DateTime.Now;
    }

    public readonly StatsApi.GeneralSpecs CachedGeneralSpecs = StatsApi.GetGeneralSpecs();
    public StatsApi.CpuData? CachedCpuStats { get; private set; }
    public List<GpuHandling.GpuData>? CachedGpuStats { get; private set; }
    public StatsApi.MemoryData? CachedMemoryStats { get; private set; }
    public List<DiskHandling.DiskData>? CachedWatchedDiskData { get; private set; }


    // Methods to update data

    public void CheckCpuData()
    {
        CachedCpuStats = StatsApi.GetCpuData();
    }

    public void CheckGpuData()
    {
        CachedGpuStats = GpuHandling.GetGpuDataList();
    }

    public void CheckMemoryData()
    {
        CachedMemoryStats = StatsApi.GetMemoryData();
    }

    public void CheckDiskData()
    {
        CachedWatchedDiskData = DiskHandling.GetDiskDatas();
    }
}