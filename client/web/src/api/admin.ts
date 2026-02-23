import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || "";

export type JobSchedule = {
	jobName: string;
	source: string;
	scheduleExpression: string;
	timezone: string;
	enabled: boolean;
	description?: string | null;
	nextFireTimeUtc?: string | null;
	lastRunTimeUtc?: string | null;
	lastRunStatus?: string | null;
	lastRunDurationMs?: number | null;
	lastRunErrorMessage?: string | null;
};

export type JobRun = {
	id: number;
	scheduledTime?: string | null;
	startTime: string;
	endTime?: string | null;
	status: string;
	durationMs?: number | null;
	errorMessage?: string | null;
	requestId?: string | null;
	environment?: string | null;
};

export type JobRunStats = {
	jobName: string;
	source: string;
	rangeHours: number;
	totalRuns: number;
	failedRuns: number;
	successRuns: number;
	failureRate: number;
	averageDurationMs: number;
};

export type AdminFailure = {
	jobName: string;
	source: string;
	startTimeUtc: string;
	errorMessage?: string | null;
};

export type AdminHealth = {
	rangeHours: number;
	totalRuns: number;
	failedRuns: number;
	successRuns: number;
	failureRate: number;
	lastFailureAtUtc?: string | null;
	recentFailures: AdminFailure[];
};

export async function fetchAdminSchedules(source?: string): Promise<JobSchedule[]> {
	const params = source ? { source } : undefined;
	const res = await axios.get(`${API_BASE}/api/admin/schedules`, { params });
	return res.data;
}

export async function fetchAdminRuns(
	jobName: string,
	source: string,
	limit = 200,
): Promise<JobRun[]> {
	const res = await axios.get(
		`${API_BASE}/api/admin/schedules/${encodeURIComponent(jobName)}/runs`,
		{ params: { source, limit } },
	);
	return res.data;
}

export async function fetchAdminStats(
	jobName: string,
	source: string,
	rangeHours = 24,
): Promise<JobRunStats> {
	const res = await axios.get(
		`${API_BASE}/api/admin/schedules/${encodeURIComponent(jobName)}/stats`,
		{ params: { source, rangeHours } },
	);
	return res.data;
}

export async function fetchAdminHealth(rangeHours = 24): Promise<AdminHealth> {
	const res = await axios.get(`${API_BASE}/api/admin/health`, {
		params: { rangeHours },
	});
	return res.data;
}

export type MatchRunRequest = {
	sourceShop: number;
	targetShop: number;
	mode?: string;
	since?: string | null;
	limit?: number;
	topN?: number;
	useLlm?: boolean;
	force?: boolean;
};

export type MatchJobStatus = {
	id: string;
	sourceShop: number;
	targetShop: number;
	status: string;
	mode: string;
	since?: string | null;
	total: number;
	processed: number;
	matched: number;
	comparable: number;
	failed: number;
	useLlm: boolean;
	limitNum: number;
	topN: number;
	errorMessage?: string | null;
	createdAt: string;
	updatedAt: string;
};

export async function runMatchJob(
	payload: MatchRunRequest,
): Promise<{ jobId: string }> {
	const res = await axios.post(`${API_BASE}/api/Match/run`, payload);
	return res.data;
}

export async function fetchMatchJobStatus(
	jobId: string,
): Promise<MatchJobStatus> {
	const res = await axios.get(
		`${API_BASE}/api/Match/status/${encodeURIComponent(jobId)}`,
	);
	return res.data;
}
