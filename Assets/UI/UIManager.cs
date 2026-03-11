using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("References")]
    public GameManager gameManager;

    [Header("Panel Text References")]
    public TextMeshProUGUI summaryText;   // 顶部总览
    public TextMeshProUGUI auctionText;   // 左上拍卖状态
    public TextMeshProUGUI agentText;     // 右上智能体状态
    public TextMeshProUGUI taskText;      // 右下任务状态
    public TextMeshProUGUI historyText;   // 底部日志历史

    [Header("Display Settings")]
    public int maxTaskDisplayCount = 8;
    public int maxHistoryDisplayCount = 6;

    void Update()
    {
        if (gameManager == null) return;

        UpdateSummaryPanel();
        UpdateAuctionPanel();
        UpdateAgentPanel();
        UpdateTaskPanel();
        UpdateHistoryPanel();
    }

    void UpdateSummaryPanel()
    {
        if (summaryText == null) return;

        string content = "";
        content += "SYSTEM OVERVIEW\n";
        content += $"Tasks: {gameManager.completedTaskCount}/{gameManager.totalTaskCount}    ";
        content += $"Rate: {gameManager.GetCompletionRate() * 100f:F1}%    ";
        content += $"Failed: {gameManager.failedAgentCount}    ";
        content += $"Time: {gameManager.GetTotalCompletionTime():F1}s\n";

        content += $"Assign: {gameManager.totalAssignmentCount}    ";
        content += $"Fairness: {gameManager.GetFairnessIndex():F3}    ";
        content += $"Reauction: {gameManager.totalReauctionCount}\n";

        content += $"Consensus S/F: {gameManager.totalConsensusSuccess}/{gameManager.totalConsensusFail}    ";
        content += $"Msgs: {gameManager.totalMessageCount}";

        summaryText.text = content;
    }

    void UpdateAuctionPanel()
    {
        if (auctionText == null) return;

        string content = "";
        content += "AUCTION STATUS\n";
        content += $"Current Task   : {gameManager.currentAuctionTaskId}\n";
        content += $"Current Winner : {gameManager.currentWinnerAgentId}\n";
        content += $"Task Announce  : {gameManager.taskAnnouncementCount}\n";
        content += $"Bid Msg        : {gameManager.bidMessageCount}\n";
        content += $"Evaluation     : {gameManager.evaluationCount}\n";
        content += $"Confirm Msg    : {gameManager.confirmMessageCount}\n";
        content += $"Reassign Msg   : {gameManager.reassignMessageCount}\n";
        content += "\n";

        if (!string.IsNullOrEmpty(gameManager.auctionInfo))
            content += gameManager.auctionInfo;
        else
            content += "No active auction.";

        auctionText.text = content;
    }

    void UpdateAgentPanel()
    {
        if (agentText == null) return;

        string content = "";
        content += "AGENT STATUS\n";

        if (gameManager.agents == null || gameManager.agents.Length == 0)
        {
            content += "No agents.";
            agentText.text = content;
            return;
        }

        foreach (AgentController agent in gameManager.agents)
        {
            if (agent == null) continue;

            string state = GetAgentStateText(agent);

            content +=
                $"A{agent.agentId} [{agent.agentType}] {state}\n" +
                $"  Load: {agent.currentLoad}/{agent.maxLoad}  " +
                $"Done: {agent.completedTaskCount}\n" +
                $"  Cap: {agent.capabilityScore:F1}  " +
                $"RiskTol: {agent.riskTolerance:F1}\n";
        }

        agentText.text = content;
    }

    void UpdateTaskPanel()
    {
        if (taskText == null) return;

        string content = "";
        content += "TASK STATUS\n";

        TaskPoint[] taskPoints = FindObjectsOfType<TaskPoint>();
        if (taskPoints == null || taskPoints.Length == 0)
        {
            content += "No tasks.";
            taskText.text = content;
            return;
        }

        System.Array.Sort(taskPoints, (a, b) => a.taskId.CompareTo(b.taskId));

        int displayCount = Mathf.Min(taskPoints.Length, maxTaskDisplayCount);

        for (int i = 0; i < displayCount; i++)
        {
            TaskPoint task = taskPoints[i];
            if (task == null) continue;

            string urgentText = task.isUrgent ? "URG" : "NOR";
            string reassignText = task.hasBeenReassigned ? "R" : "-";

            content +=
                $"T{task.taskId} {task.GetStateText()} [{task.requiredAgentType}] {urgentText} {reassignText}\n" +
                $"  Risk:{task.riskLevel:F1}  Round:{task.auctionRound}  " +
                $"P:{task.proposedWinnerId} C:{task.confirmedWinnerId}\n" +
                $"  Confirm:{task.confirmCount}/{task.requiredConfirmations}  " +
                $"ReCnt:{task.reassignCount}\n";
        }

        if (taskPoints.Length > displayCount)
        {
            content += $"\n... {taskPoints.Length - displayCount} more tasks";
        }

        taskText.text = content;
    }

    void UpdateHistoryPanel()
    {
        if (historyText == null) return;

        string content = "";
        content += "RECENT HISTORY\n";

        if (gameManager.auctionHistory == null || gameManager.auctionHistory.Count == 0)
        {
            content += "No auction history yet.\n";
            content += "\nControls: Press 1 / 2 / 3 to fail agents.";
            historyText.text = content;
            return;
        }

        int startIndex = Mathf.Max(0, gameManager.auctionHistory.Count - maxHistoryDisplayCount);

        for (int i = gameManager.auctionHistory.Count - 1; i >= startIndex; i--)
        {
            content += $"• {gameManager.auctionHistory[i]}\n";
        }

        content += "\nControls: Press 1 / 2 / 3 to fail agents.";

        historyText.text = content;
    }

    string GetAgentStateText(AgentController agent)
    {
        if (agent == null) return "NULL";

        if (agent.isFailed)
            return "FAILED";

        if (agent.hasConfirmedTask)
            return "EXEC";

        if (agent.isProposedWinner)
            return "PROPOSED";

        if (agent.IsBusy())
            return "BUSY";

        return "IDLE";
    }
}