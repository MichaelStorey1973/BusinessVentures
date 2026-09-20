using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AnalysisServices.AdomdClient;
using TabularEditor.TOMWrapper;

public string PBIServer =
    "DataSource=powerbi://api.powerbi.com/v1.0/myorg/TKV-BusinessVentures-PRD";

public string PBIServerName =
    "powerbi://api.powerbi.com/v1.0/myorg/TKV-BusinessVentures-PRD";

public string PBIDatabase = "TKV-BusinessVentures";

List<PartitionProcessingStatus> processingStatus =
    GetProcessingStatus(Model);


Output(processingStatus); 

//foreach (PartitionProcessingStatus item in processingStatus)
//{
//    Output(
//        item.TableName
//        + " | "
//        + item.TableGroup
//        + " | "
//        + item.PartitionName
//        + " | "
//        + item.State
//        + " | "
//        + item.RefreshedTimeText
//        + " | "
//        + item.Status
//        + (String.IsNullOrEmpty(item.ErrorMessage)
//            ? String.Empty
//            : " | " + item.ErrorMessage));
//}
//
public class PartitionProcessingStatus
{
    public string TableName { get; set; }
    public string TableGroup { get; set; }
    public string PartitionName { get; set; }
    public string State { get; set; }
    public DateTime RefreshedTime { get; set; }
    public string RefreshedTimeText { get; set; }
    public string Status { get; set; }
    public string ErrorMessage { get; set; }
}

public List<PartitionProcessingStatus> GetProcessingStatus(Model m )
{
    var result = new List<PartitionProcessingStatus>();

    // Model, Tables, TableGroup and Partitions are Tabular Editor TOMWrapper
    // objects supplied by the C# scripting host.
    foreach (TabularEditor.TOMWrapper.Table table in Model.Tables)
    {
        string tableGroup = table.TableGroup ?? String.Empty;

        if (!tableGroup.StartsWith(
                @"FACTS\",
                StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        foreach (TabularEditor.TOMWrapper.Partition partition
            in table.Partitions)
        {
            string state = partition.State.ToString();
            DateTime refreshedTime = partition.RefreshedTime;

            result.Add(new PartitionProcessingStatus
            {
                TableName = table.Name,
                TableGroup = tableGroup,
                PartitionName = partition.Name,
                State = state,
                RefreshedTime = refreshedTime,
                RefreshedTimeText = refreshedTime == DateTime.MinValue
                    ? String.Empty
                    : refreshedTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Status = state.Equals(
                        "Ready",
                        StringComparison.OrdinalIgnoreCase)
                    ? "Succeeded"
                    : state,
                ErrorMessage = partition.ErrorMessage ?? String.Empty
            });
        }
    }

    return result;
}

public List<int> GetRDS(string server, string db)
{
    var reportingDates = new List<int>();

    string dax = @"
EVALUATE
VAR CurrentFY =
    MAXX(
        TOPN(
            1,
            ALL('REPORTING DATE'),
            'REPORTING DATE'[ID], DESC
        ),
        'REPORTING DATE'[REPORTING FISCAL YEAR]
    )
VAR PreviousFY =
    MAXX(
        FILTER(
            ALL('REPORTING DATE'),
            'REPORTING DATE'[REPORTING FISCAL YEAR] <> CurrentFY
        ),
        'REPORTING DATE'[REPORTING FISCAL YEAR]
    )
VAR PreviousFYLastID =
    MAXX(
        FILTER(
            ALL('REPORTING DATE'),
            'REPORTING DATE'[REPORTING FISCAL YEAR] = PreviousFY
        ),
        'REPORTING DATE'[ID]
    )
RETURN
    TOPN(
        350,
        FILTER(
            SUMMARIZECOLUMNS(
                'REPORTING DATE'[ID],
                'REPORTING DATE'[REPORTING FISCAL YEAR]
            ),
            'REPORTING DATE'[REPORTING FISCAL YEAR] = CurrentFY
                || 'REPORTING DATE'[ID] = PreviousFYLastID
        ),
        'REPORTING DATE'[ID], DESC
    )";

    using (var conn = new AdomdConnection(
        "Data Source=arp_tab_prod;Initial Catalog=ARP;"))
    {
        conn.Open();

        using (var cmd = new AdomdCommand(dax, conn))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                reportingDates.Add(Convert.ToInt32(reader[0]));
            }
        }
    }

    return reportingDates;
}
