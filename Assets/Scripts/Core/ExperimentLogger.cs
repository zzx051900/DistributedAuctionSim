using System.IO;
using UnityEngine;

public class ExperimentLogger : MonoBehaviour
{
    public static ExperimentLogger Instance;

    private string summaryFilePath;
    private string detailFilePath;

    private int runId = 1;
    private int auctionRound = 0;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        string folderPath = Path.Combine(Application.dataPath, "../ExperimentResults");

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        summaryFilePath = Path.Combine(folderPath, "summary_results.csv");
        detailFilePath = Path.Combine(folderPath, "auction_details.csv");

        // 每次启动直接覆盖旧日志
        CreateOrResetSummaryFile();
        CreateOrResetDetailFile();

        Debug.Log("ExperimentLogger initialized.");
        Debug.Log("Summary file: " + summaryFilePath);
        Debug.Log("Detail file: " + detailFilePath);
    }

    void CreateOrResetSummaryFile()
    {
        File.WriteAllText(
            summaryFilePath,
            "runId," +
            "taskCount,completedTaskCount,completionRate,totalAssignmentCount,failedAgentCount,totalCompletionTime,fairnessIndex," +
            "totalConsensusSuccess,totalConsensusFail,totalReauctionCount," +
            "taskAnnouncementCount,bidMessageCount,evaluationCount,confirmMessageCount,reassignMessageCount,totalMessageCount," +
            "tasksNeedingReassignment,successfulReassignments,failedReassignments,reassignmentSuccessRate," +
            "avgTaskLifecycleTime,avgAuctionToConfirmDelay,avgConfirmToExecutionDelay,avgExecutionDuration," +
            "agent0Type,agent0Completed,agent1Type,agent1Completed,agent2Type,agent2Completed\n"
        );
    }

    void CreateOrResetDetailFile()
    {
        File.WriteAllText(
            detailFilePath,
            "runId,round,time," +
            "taskId,taskType,riskLevel,isUrgent," +
            "triggerReason,stage," +
            "agent0State,agent0Bid,agent1State,agent1Bid,agent2State,agent2Bid," +
            "proposedWinnerId,confirmedWinnerId,winnerBid,confirmCount,requiredConfirmations,consensusResult,isReauction," +
            "hasBeenReassigned,reassignCount\n"
        );
    }

    public int GetRunId()
    {
        return runId;
    }

    public int GetNextAuctionRound()
    {
        auctionRound++;
        return auctionRound;
    }

    public void ResetAuctionRound()
    {
        auctionRound = 0;
    }

    public void NextRun()
    {
        runId++;
        auctionRound = 0;
    }

    public void LogSummary(GameManager gm)
    {
        if (gm == null) return;

        string a0Type = (gm.agents != null && gm.agents.Length > 0 && gm.agents[0] != null)
            ? gm.agents[0].agentType.ToString() : "NONE";
        string a1Type = (gm.agents != null && gm.agents.Length > 1 && gm.agents[1] != null)
            ? gm.agents[1].agentType.ToString() : "NONE";
        string a2Type = (gm.agents != null && gm.agents.Length > 2 && gm.agents[2] != null)
            ? gm.agents[2].agentType.ToString() : "NONE";

        int a0Completed = (gm.agents != null && gm.agents.Length > 0 && gm.agents[0] != null)
            ? gm.agents[0].completedTaskCount : 0;
        int a1Completed = (gm.agents != null && gm.agents.Length > 1 && gm.agents[1] != null)
            ? gm.agents[1].completedTaskCount : 0;
        int a2Completed = (gm.agents != null && gm.agents.Length > 2 && gm.agents[2] != null)
            ? gm.agents[2].completedTaskCount : 0;

        string line =
            $"{runId}," +
            $"{gm.totalTaskCount}," +
            $"{gm.completedTaskCount}," +
            $"{gm.GetCompletionRate():F4}," +
            $"{gm.totalAssignmentCount}," +
            $"{gm.failedAgentCount}," +
            $"{gm.GetTotalCompletionTime():F4}," +
            $"{gm.GetFairnessIndex():F4}," +
            $"{gm.totalConsensusSuccess}," +
            $"{gm.totalConsensusFail}," +
            $"{gm.totalReauctionCount}," +
            $"{gm.taskAnnouncementCount}," +
            $"{gm.bidMessageCount}," +
            $"{gm.evaluationCount}," +
            $"{gm.confirmMessageCount}," +
            $"{gm.reassignMessageCount}," +
            $"{gm.totalMessageCount}," +
            $"{gm.tasksNeedingReassignment}," +
            $"{gm.successfulReassignments}," +
            $"{gm.failedReassignments}," +
            $"{gm.GetReassignmentSuccessRate():F4}," +
            $"{gm.GetAverageTaskLifecycleTime():F4}," +
            $"{gm.GetAverageAuctionToConfirmDelay():F4}," +
            $"{gm.GetAverageConfirmToExecutionDelay():F4}," +
            $"{gm.GetAverageExecutionDuration():F4}," +
            $"{a0Type},{a0Completed}," +
            $"{a1Type},{a1Completed}," +
            $"{a2Type},{a2Completed}\n";

        File.AppendAllText(summaryFilePath, line);
        Debug.Log("Summary exported.");
    }

    // 兼容旧版调用
    public void LogAuctionDetail(
        float time,
        int taskId,
        string triggerReason,
        string agent0State, string agent0Bid,
        string agent1State, string agent1Bid,
        string agent2State, string agent2Bid,
        int winnerAgentId,
        string winnerBid)
    {
        LogAuctionDetailExtended(
            time,
            taskId,
            "UNKNOWN",
            0f,
            false,
            triggerReason,
            "BIDDING",
            agent0State, agent0Bid,
            agent1State, agent1Bid,
            agent2State, agent2Bid,
            winnerAgentId,
            -1,
            winnerBid,
            0,
            0,
            "PENDING",
            false,
            0,
            0,
            GetNextAuctionRound()
        );
    }

    public void LogAuctionDetailExtended(
        float time,
        int taskId,
        string taskType,
        float riskLevel,
        bool isUrgent,
        string triggerReason,
        string stage,
        string agent0State, string agent0Bid,
        string agent1State, string agent1Bid,
        string agent2State, string agent2Bid,
        int proposedWinnerId,
        int confirmedWinnerId,
        string winnerBid,
        int confirmCount,
        int requiredConfirmations,
        string consensusResult,
        bool isReauction,
        int hasBeenReassigned,
        int reassignCount,
        int round)
    {
        string safeTaskType = string.IsNullOrEmpty(taskType) ? "UNKNOWN" : taskType;
        string urgencyText = isUrgent ? "1" : "0";

        string safeTriggerReason = string.IsNullOrEmpty(triggerReason) ? "NORMAL" : triggerReason;
        string safeStage = string.IsNullOrEmpty(stage) ? "UNKNOWN" : stage;

        string safeAgent0State = string.IsNullOrEmpty(agent0State) ? "NONE" : agent0State;
        string safeAgent1State = string.IsNullOrEmpty(agent1State) ? "NONE" : agent1State;
        string safeAgent2State = string.IsNullOrEmpty(agent2State) ? "NONE" : agent2State;

        string safeAgent0Bid = string.IsNullOrEmpty(agent0Bid) ? "" : agent0Bid;
        string safeAgent1Bid = string.IsNullOrEmpty(agent1Bid) ? "" : agent1Bid;
        string safeAgent2Bid = string.IsNullOrEmpty(agent2Bid) ? "" : agent2Bid;
        string safeWinnerBid = string.IsNullOrEmpty(winnerBid) ? "" : winnerBid;

        string safeConsensusResult = string.IsNullOrEmpty(consensusResult) ? "UNKNOWN" : consensusResult;
        string reauctionText = isReauction ? "1" : "0";

        string line =
            $"{runId}," +
            $"{round}," +
            $"{time:F4}," +
            $"{taskId}," +
            $"{safeTaskType}," +
            $"{riskLevel:F2}," +
            $"{urgencyText}," +
            $"{safeTriggerReason}," +
            $"{safeStage}," +
            $"{safeAgent0State},{safeAgent0Bid}," +
            $"{safeAgent1State},{safeAgent1Bid}," +
            $"{safeAgent2State},{safeAgent2Bid}," +
            $"{proposedWinnerId}," +
            $"{confirmedWinnerId}," +
            $"{safeWinnerBid}," +
            $"{confirmCount}," +
            $"{requiredConfirmations}," +
            $"{safeConsensusResult}," +
            $"{reauctionText}," +
            $"{hasBeenReassigned}," +
            $"{reassignCount}\n";

        File.AppendAllText(detailFilePath, line);
    }
}