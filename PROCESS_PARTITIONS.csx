using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AnalysisServices.Tabular;
using Microsoft.AnalysisServices.AdomdClient;

public string PBIServer =
    "DataSource=powerbi://api.powerbi.com/v1.0/myorg/TKV-BusinessVentures-PRD";

public string PBIServerName =
    "powerbi://api.powerbi.com/v1.0/myorg/TKV-BusinessVentures-PRD";

public string PBIDatabase = "TKV-BusinessVentures";

Output(GetDBName(PBIServer, PBIDatabase));

List<PartitionProcessingStatus> processingStatus =
    GetProcessingStatus(PBIServer, PBIDatabase);

foreach (var item in processingStatus)
{
    Output(
        item.TableName
        + " | "
        + item.PartitionName
        + " | "
        + item.State
        + " | "
        + item.RefreshedTimeText
        + " | "
        + item.Status
        + (String.IsNullOrEmpty(item.ErrorMessage)
            ? String.Empty
            : " | " + item.ErrorMessage));
}

public Microsoft.AnalysisServices.Tabular.Database GetDBName(
    string serverConnectionString,
    string databaseName)
{
    var server = new Microsoft.AnalysisServices.Tabular.Server();

    try
    {
        server.Connect(serverConnectionString);

        return server.Databases
            .Cast<Microsoft.AnalysisServices.Tabular.Database>()
            .FirstOrDefault(d =>
                d.Name.StartsWith(
                    databaseName,
                    StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        if (server.Connected)
        {
            server.Disconnect();
        }
    }
}

public class PartitionProcessingStatus
{
    public string TableName { get; set; }
    public string DisplayFolder { get; set; }
    public string PartitionName { get; set; }
    public string State { get; set; }
    public DateTime? RefreshedTime { get; set; }
    public string RefreshedTimeText { get; set; }
    public string Status { get; set; }
    public string ErrorMessage { get; set; }
}

public List<PartitionProcessingStatus> GetProcessingStatus(
    string serverConnectionString,
    string databaseName)
{
    var result = new List<PartitionProcessingStatus>();
    var server = new Microsoft.AnalysisServices.Tabular.Server();

    try
    {
        server.Connect(serverConnectionString);

        var database = server.Databases
            .Cast<Microsoft.AnalysisServices.Tabular.Database>()
            .FirstOrDefault(d =>
                d.Name.Equals(
                    databaseName,
                    StringComparison.OrdinalIgnoreCase)
                || d.Name.StartsWith(
                    databaseName,
                    StringComparison.OrdinalIgnoreCase));

        if (database == null)
        {
            throw new Exception(
                "Semantic model not found: " + databaseName);
        }

        foreach (var table in database.Model.Tables)
        {
            var displayFolder = table.DisplayFolder ?? String.Empty;

            if (!displayFolder.StartsWith(
                    @"FACT\",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var partition in table.Partitions)
            {
                var state = partition.State.ToString();
                var refreshedTime = partition.RefreshedTime;

                result.Add(new PartitionProcessingStatus
                {
                    TableName = table.Name,
                    DisplayFolder = displayFolder,
                    PartitionName = partition.Name,
                    State = state,
                    RefreshedTime = refreshedTime,
                    RefreshedTimeText = refreshedTime == null
                        ? String.Empty
                        : refreshedTime.Value.ToString(
                            "yyyy-MM-dd HH:mm:ss"),
                    Status = state.Equals(
                            "Ready",
                            StringComparison.OrdinalIgnoreCase)
                        ? "Succeeded"
                        : state,
                    ErrorMessage = String.Empty
                });
            }
        }
    }
    catch (Exception ex)
    {
        result.Add(new PartitionProcessingStatus
        {
            TableName = String.Empty,
            DisplayFolder = String.Empty,
            PartitionName = String.Empty,
            State = "Error",
            Status = "Error",
            ErrorMessage = ex.ToString()
        });
    }
    finally
    {
        if (server.Connected)
        {
            server.Disconnect();
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
