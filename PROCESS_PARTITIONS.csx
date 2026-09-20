using System.IO; 
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AnalysisServices;
using Microsoft.AnalysisServices.AdomdClient;
using TabularEditor.TOMWrapper;




public string PBIServer =
    "DataSource=powerbi://api.powerbi.com/v1.0/myorg/TKV-BusinessVentures-PRD";

public string PBIServerName =
    "powerbi://api.powerbi.com/v1.0/myorg/TKV-BusinessVentures-PRD";

public string PBIDatabase = "TKV-BusinessVentures";

List<PartitionProcessingStatus> processingStatus =
    GetProcessingStatus(Model);

var x = processingStatus.Where(
n=>n.PartitionName.Contains("20260904") && !n.PartitionName.Contains("LTE"))
.ToList() ; 

/* 

public string ProcessPartitions(
    string serverConnectionString,
    string databaseName,
    List<PartitionProcessingStatus> partitionsToProcess,
    int batchSize = 64)

*/ 
Logger.Log("Start Processing 20260904");
ProcessPartitions(
PBIServer, 
PBIDatabase, 
x,
5 
); 
Logger.Log("End Processing 20260904");

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






public string ProcessPartitions(
    string serverConnectionString,
    string databaseName,
    List<PartitionProcessingStatus> partitionsToProcess,
    int batchSize = 64)
{
    if (partitionsToProcess == null ||
        partitionsToProcess.Count == 0)
    {
        return "No partitions were supplied for processing.";
    }

    if (batchSize <= 0)
    {
        batchSize = 64;
    }

    var server =
        new Microsoft.AnalysisServices.Tabular.Server();

    var processedCount = 0;
    var skippedCount = 0;
    var errors = new List<string>();

    try
    {
        server.Connect(
            serverConnectionString
            + ";Application Name=Tabular Editor Partition Processing");

        Microsoft.AnalysisServices.Tabular.Database database =
            server.Databases
                .Cast<Microsoft.AnalysisServices.Tabular.Database>()
                .FirstOrDefault(d =>
                    d.Name.StartsWith(
                        databaseName,
                        StringComparison.OrdinalIgnoreCase));

        if (database == null)
        {
            throw new Exception(
                "Database not found: " + databaseName);
        }

        Microsoft.AnalysisServices.Tabular.Model model =
            database.Model;

        // Resolve the supplied status objects to Analysis Services TOM
        // partitions before submitting any processing commands.
        var partitions = new List<
            Microsoft.AnalysisServices.Tabular.Partition>();

        var partitionKeys =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (PartitionProcessingStatus item
            in partitionsToProcess)
        {
            if (item == null ||
                String.IsNullOrWhiteSpace(item.TableName) ||
                String.IsNullOrWhiteSpace(item.PartitionName))
            {
                skippedCount++;

                errors.Add(
                    "A status item is missing TableName or PartitionName.");

                continue;
            }

            Microsoft.AnalysisServices.Tabular.Table table =
                model.Tables[item.TableName];

            if (table == null)
            {
                skippedCount++;

                errors.Add(
                    "Table not found: " + item.TableName);

                continue;
            }

            Microsoft.AnalysisServices.Tabular.Partition partition =
                table.Partitions[item.PartitionName];

            if (partition == null)
            {
                skippedCount++;

                errors.Add(
                    "Partition not found: "
                    + item.TableName
                    + "."
                    + item.PartitionName);

                continue;
            }

            string partitionKey =
                item.TableName
                + "\u001F"
                + item.PartitionName;

            if (partitionKeys.Add(partitionKey))
            {
                partitions.Add(partition);
            }
            else
            {
                skippedCount++;
            }
        }

        // Process in batches. RequestRefresh queues the command on the
        // Analysis Services TOM object. SaveChanges executes the batch.
        for (int i = 0; i < partitions.Count; i += batchSize)
        {
            List<
                Microsoft.AnalysisServices.Tabular.Partition> batch =
                partitions
                    .Skip(i)
                    .Take(batchSize)
                    .ToList();

            foreach (
                Microsoft.AnalysisServices.Tabular.Partition partition
                in batch)
            {
                partition.RequestRefresh(
                    Microsoft.AnalysisServices.Tabular.RefreshType.Full);

                Logger.LogDetail(
                    "Queued partition: "
                    + partition.Table.Name
                    + "."
                    + partition.Name);
            }

            model.SaveChanges();

            processedCount += batch.Count;

            Logger.Log(
                "Processed batch of "
                + batch.Count
                + " partition(s).");
        }

        string result =
            processedCount
            + " partition(s) processed successfully.";

        if (skippedCount > 0)
        {
            result +=
                " "
                + skippedCount
                + " item(s) skipped.";

            if (errors.Count > 0)
            {
                result +=
                    " Details: "
                    + String.Join("; ", errors);
            }
        }

        return result;
    }
    finally
    {
        if (server.Connected)
        {
            server.Disconnect();
        }
    }
}




public static class Logger
{
    private static readonly string LogFile =
        @"C:\Temp\TabularEditor.log";

    public static void Log(string message)
    {
        File.AppendAllText(
            LogFile,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}"
        );
    }

 private static readonly string LogFileDetails =
        @"C:\Temp\TabularEditorDetail.log";

    public static void LogDetail(string message)
    {
        File.AppendAllText(
            LogFileDetails,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}"
        );
    }


}
