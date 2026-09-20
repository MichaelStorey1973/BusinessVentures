using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.AnalysisServices.Core;
using Microsoft.AnalysisServices.Tabular ;
using TabularEditor.Shared.Services;
using Microsoft.AnalysisServices.AdomdClient;
using System.Runtime.Intrinsics.Wasm;



public string PBIServer = "DataSource=powerbi://api.powerbi.com/v1.0/myorg/TKV-BusinessVentures-PRD";
public string PBIServerName = "powerbi://api.powerbi.com/v1.0/myorg/TKV-BusinessVentures-PRD";
// public string PBIDatabase = "Power BI Model With CF And Adjustments ORI CMT_DEV";
public string PBIDatabase = "TKV-BusinessVentures";



Output(GetDBName(PBIServer , PBIDatabase));


List<ProcessingStatus> PS = GetProcessingStatus ( PBIServerName  , PBIDatabase );




public Microsoft.AnalysisServices.Tabular.Database GetDBName( string PBIServer , string DBIName )
{

Microsoft.AnalysisServices.Tabular.Server  server = new Microsoft.AnalysisServices.Tabular.Server();
server.Connect(PBIServer);

var dbName  = server.Databases
               .Cast<Microsoft.AnalysisServices.Tabular.Database>()
               .FirstOrDefault(d => d.Name.StartsWith(DBIName));

return dbName; 

}



 public  List<int> GetRDS(string server, string db)
      {

          var reportingDates = new List<int>();

          string dax = @"
                EVALUATE

                VAR CurrentFY =
                    MAXX (
                        TOPN (
                            1,
                            ALL ( 'REPORTING DATE' ),
                            'REPORTING DATE'[ID], DESC
                        ),
                        'REPORTING DATE'[REPORTING FISCAL YEAR]
                    )

                VAR PreviousFY =
                    MAXX (
                        FILTER (
                            ALL ( 'REPORTING DATE' ),
                            'REPORTING DATE'[REPORTING FISCAL YEAR] <> CurrentFY
                        ),
                        'REPORTING DATE'[REPORTING FISCAL YEAR]
                    )

                VAR PreviousFYLastID =
                    MAXX (
                        FILTER (
                            ALL ( 'REPORTING DATE' ),
                            'REPORTING DATE'[REPORTING FISCAL YEAR] = PreviousFY
                        ),
                        'REPORTING DATE'[ID]
                    )

                RETURN
                TOPN (
                    350,
                    FILTER (
                        SUMMARIZECOLUMNS (
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
