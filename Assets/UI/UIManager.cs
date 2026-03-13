using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("References")]
    public GameManager gameManager;

    [Header("Panel Text References")]
    public TextMeshProUGUI summaryText;   // 顶部总览
    public TextMeshProUGUI auctionText;   // 中下拍卖状态
    public TextMeshProUGUI agentText;     // 右上智能体状态
    public TextMeshProUGUI taskText;      // 右下任务状态
    public TextMeshProUGUI historyText;   // 左侧最近事件

    [Header("Display Settings")]
    public int maxTaskDisplayCount = 6;
    public int maxHistoryDisplayCount = 5;

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
        content += "系统总览\n";
        content += $"实验类型：{GetExperimentTypeText(gameManager.GetExperimentTypeString())}\n";
        content += $"任务：{gameManager.completedTaskCount}/{gameManager.totalTaskCount}    ";
        content += $"完成率：{gameManager.GetCompletionRate() * 100f:F1}%    ";
        content += $"运行时间：{gameManager.GetTotalCompletionTime():F1}s\n";

        content += $"分配次数：{gameManager.totalAssignmentCount}    ";
        content += $"重拍次数：{gameManager.totalReauctionCount}    ";
        content += $"失效节点：{gameManager.failedAgentCount}    ";
        content += $"断连次数：{gameManager.disconnectedEventCount}\n";

        content += $"公平性：{gameManager.GetFairnessIndex():F3}    ";
        content += $"共识成功/失败：{gameManager.totalConsensusSuccess}/{gameManager.totalConsensusFail}\n";

        content += $"消息总数：{gameManager.totalMessageCount}    ";
        content += $"重分配成功率：{gameManager.GetReassignmentSuccessRate() * 100f:F1}%";

        summaryText.text = content;
    }

    void UpdateAuctionPanel()
    {
        if (auctionText == null) return;

        string content = "";
        content += "拍卖状态\n";

        string currentTaskText = gameManager.currentAuctionTaskId >= 0
            ? $"T{gameManager.currentAuctionTaskId}"
            : "无";

        string currentWinnerText = gameManager.currentWinnerAgentId >= 0
            ? $"A{gameManager.currentWinnerAgentId}"
            : "无";

        content += $"当前任务：{currentTaskText}    当前赢家：{currentWinnerText}\n";
        content += $"任务发布：{gameManager.taskAnnouncementCount}    投标消息：{gameManager.bidMessageCount}    评标次数：{gameManager.evaluationCount}\n";
        content += $"确认消息：{gameManager.confirmMessageCount}    重分配消息：{gameManager.reassignMessageCount}    总消息：{gameManager.totalMessageCount}\n";

        if (!string.IsNullOrEmpty(gameManager.auctionInfo))
        {
            content += "\n当前说明：\n";
            content += SimplifyAuctionInfo(gameManager.auctionInfo);
        }
        else
        {
            content += "\n当前说明：暂无活跃拍卖";
        }

        auctionText.text = content;
    }

    void UpdateAgentPanel()
    {
        if (agentText == null) return;

        string content = "";
        content += "智能体状态\n";

        if (gameManager.agents == null || gameManager.agents.Length == 0)
        {
            content += "暂无智能体";
            agentText.text = content;
            return;
        }

        foreach (AgentController agent in gameManager.agents)
        {
            if (agent == null) continue;

            string typeText = GetAgentTypeText(agent.agentType.ToString());
            string stateText = GetAgentStateText(agent);

            content += $"A{agent.agentId} {typeText}｜{stateText}｜负载 {agent.currentLoad}/{agent.maxLoad}｜完成 {agent.completedTaskCount}\n";
            content += $"能力 {agent.capabilityScore:F1}｜风险容忍 {agent.riskTolerance:F1}\n";
        }

        agentText.text = content;
    }

    void UpdateTaskPanel()
    {
        if (taskText == null) return;

        string content = "";
        content += "任务状态\n";

        TaskPoint[] taskPoints = FindObjectsOfType<TaskPoint>();
        if (taskPoints == null || taskPoints.Length == 0)
        {
            content += "暂无任务";
            taskText.text = content;
            return;
        }

        System.Array.Sort(taskPoints, (a, b) => a.taskId.CompareTo(b.taskId));

        int displayCount = Mathf.Min(taskPoints.Length, maxTaskDisplayCount);

        for (int i = 0; i < displayCount; i++)
        {
            TaskPoint task = taskPoints[i];
            if (task == null) continue;

            string stateText = GetTaskStateText(task.GetStateText());
            string typeText = GetAgentTypeText(task.requiredAgentType.ToString());
            string urgentText = task.isUrgent ? "紧急" : "普通";

            string winnerText = "-";
            if (task.confirmedWinnerId >= 0)
                winnerText = $"A{task.confirmedWinnerId}";
            else if (task.proposedWinnerId >= 0)
                winnerText = $"A{task.proposedWinnerId}";

            content += $"T{task.taskId}｜{stateText}｜{typeText}｜{urgentText}\n";
            content += $"风险 {task.riskLevel:F1}｜轮次 {task.auctionRound}｜赢家 {winnerText}｜确认 {task.confirmCount}/{task.requiredConfirmations}\n";
        }

        if (taskPoints.Length > displayCount)
        {
            content += $"\n其余 {taskPoints.Length - displayCount} 个任务未显示";
        }

        taskText.text = content;
    }

    void UpdateHistoryPanel()
    {
        if (historyText == null) return;

        string content = "";
        content += "最近事件\n";

        if (gameManager.auctionHistory == null || gameManager.auctionHistory.Count == 0)
        {
            content += "暂无事件记录\n";
            content += "\n操作提示：按 1 / 2 / 3 模拟失效，按 Q / W / E 模拟断连";
            historyText.text = content;
            return;
        }

        int startIndex = Mathf.Max(0, gameManager.auctionHistory.Count - maxHistoryDisplayCount);

        for (int i = gameManager.auctionHistory.Count - 1; i >= startIndex; i--)
        {
            content += $"• {SimplifyHistoryLine(gameManager.auctionHistory[i])}\n";
        }

        content += "\n操作提示：按 1 / 2 / 3 模拟失效，按 Q / W / E 模拟断连";

        historyText.text = content;
    }

    string GetAgentStateText(AgentController agent)
    {
        if (agent == null) return "空对象";

        if (agent.isFailed)
            return "失效";

        if (agent.isDisconnected)
            return "断连";

        if (agent.hasConfirmedTask)
            return "执行中";

        if (agent.isProposedWinner)
            return "待确认";

        if (agent.IsBusy())
            return "忙碌";

        return "空闲";
    }

    string GetAgentTypeText(string rawType)
    {
        switch (rawType)
        {
            case "Worker": return "工人";
            case "Drone": return "无人机";
            case "Car": return "车辆";
            default: return rawType;
        }
    }

    string GetExperimentTypeText(string rawType)
    {
        switch (rawType)
        {
            case "BASELINE": return "基线实验";
            case "WEAK_NETWORK": return "弱网实验";
            case "FAILURE_RECOVERY": return "失效恢复实验";
            case "CUSTOM": return "自定义实验";
            default: return rawType;
        }
    }

    string GetTaskStateText(string rawState)
    {
        switch (rawState)
        {
            case "Idle": return "空闲";
            case "Bidding": return "竞价中";
            case "Evaluating": return "评标中";
            case "Confirming": return "确认中";
            case "Committed": return "已提交";
            case "Executing": return "执行中";
            case "Completed": return "已完成";
            case "Reauctioning": return "重拍中";
            default: return rawState;
        }
    }

    string SimplifyHistoryLine(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "无";

        string s = raw;

        s = s.Replace("Round", "第");
        s = s.Replace("Task", "任务");
        s = s.Replace("Type:", "类型:");
        s = s.Replace("Risk:", "风险:");
        s = s.Replace("Urgent:", "紧急:");
        s = s.Replace("YES", "是");
        s = s.Replace("NO", "否");
        s = s.Replace("Stage:", "阶段:");
        s = s.Replace("Completed By", "完成者");
        s = s.Replace("Executor", "执行者");
        s = s.Replace("Confirmed Winner", "确认赢家");
        s = s.Replace("Proposed Winner", "候选赢家");
        s = s.Replace("Consensus Result", "共识结果");
        s = s.Replace("Reason", "原因");
        s = s.Replace("Action", "动作");
        s = s.Replace("Success", "成功");
        s = s.Replace("Fail", "失败");
        s = s.Replace("Timeout", "超时");
        s = s.Replace("Worker", "工人");
        s = s.Replace("Drone", "无人机");
        s = s.Replace("Car", "车辆");

        s = s.Replace("\n", " | ");

        if (s.Length > 60)
            s = s.Substring(0, 60) + "...";

        return s;
    }

    string SimplifyAuctionInfo(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "无";

        string s = raw;

        s = s.Replace("Round", "第");
        s = s.Replace("Task", "任务");
        s = s.Replace("Type", "类型");
        s = s.Replace("Risk", "风险");
        s = s.Replace("Urgent", "紧急");
        s = s.Replace("Trigger", "触发原因");
        s = s.Replace("Stage", "阶段");
        s = s.Replace("Winner", "赢家");
        s = s.Replace("Proposed Winner", "候选赢家");
        s = s.Replace("Confirmed Winner", "确认赢家");
        s = s.Replace("Confirm Count", "确认数");
        s = s.Replace("Consensus Result", "共识结果");
        s = s.Replace("Worker", "工人");
        s = s.Replace("Drone", "无人机");
        s = s.Replace("Car", "车辆");
        s = s.Replace("Success", "成功");
        s = s.Replace("Fail", "失败");
        s = s.Replace("Timeout", "超时");
        s = s.Replace("RUNNING", "执行中");
        s = s.Replace("PENDING", "待定");
        s = s.Replace("PROPOSED", "已提名");
        s = s.Replace("NO_CANDIDATE", "无候选者");

        return s;
    }
}