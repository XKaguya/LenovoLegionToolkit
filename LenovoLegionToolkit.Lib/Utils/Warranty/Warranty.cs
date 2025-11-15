using System.Collections.Generic;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Utils.Warranty;

public class WarrantyResponse
{
    [JsonProperty("statusCode")]
    public int StatusCode { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("data")]
    public WarrantyData Data { get; set; } = new WarrantyData();
}

public class WarrantyData
{
    [JsonProperty("baseinfo")]
    public List<WarrantyBaseInfo> BaseInfo { get; set; } = new List<WarrantyBaseInfo>();

    [JsonProperty("detailinfo")]
    public WarrantyDetailInfo DetailInfo { get; set; } = new WarrantyDetailInfo();
}

public class WarrantyDetailInfo
{
    [JsonProperty("warranty")]
    public List<WarrantyItem> Warranty { get; set; } = new List<WarrantyItem>();

    [JsonProperty("onsite")]
    public List<WarrantyItem> OnSite { get; set; } = new List<WarrantyItem>();

    [JsonProperty("other")]
    public List<WarrantyItem> Other { get; set; } = new List<WarrantyItem>();
}

public class WarrantyItem
{
    [JsonProperty("ServiceProductName")]
    public string ServiceProductName { get; set; } = string.Empty;

    [JsonProperty("ServiceProductNumber")]
    public string? ServiceProductNumber { get; set; }

    [JsonProperty("StartDate")]
    public string? StartDateString { get; set; }

    [JsonProperty("EndDate")]
    public string? EndDateString { get; set; }

    [JsonProperty("PartStartDate")]
    public string? PartStartDate { get; set; }

    [JsonProperty("PartEndDate")]
    public string? PartEndDate { get; set; }

    [JsonProperty("LaborStartDate")]
    public string? LaborStartDate { get; set; }

    [JsonProperty("LaborEndDate")]
    public string? LaborEndDate { get; set; }

    [JsonProperty("OnSiteStartDate")]
    public string? OnSiteStartDate { get; set; }

    [JsonProperty("OnSiteEndDate")]
    public string? OnSiteEndDate { get; set; }

    [JsonProperty("ServiceProductSmallClass")]
    public string? ServiceProductSmallClass { get; set; }

    [JsonProperty("Remark")]
    public string? Remark { get; set; }

    [JsonProperty("type")]
    public int? Type { get; set; }

    [JsonProperty("DateDifference")]
    public int? DateDifference { get; set; }
}

public class WarrantyBaseInfo : WarrantyItem { }