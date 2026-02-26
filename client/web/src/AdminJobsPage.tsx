import * as React from "react";
import {
	Alert,
	Box,
	Button,
	Card,
	CardContent,
	Checkbox,
	Chip,
	CircularProgress,
	Divider,
	FormControl,
	InputLabel,
	ListItemText,
	MenuItem,
	OutlinedInput,
	Select,
	Stack,
	Tab,
	Tabs,
	TextField,
	Typography,
} from "@mui/material";
import {
	DataGrid,
	type GridColDef,
	type GridRowSelectionModel,
} from "@mui/x-data-grid";
import {
	CartesianGrid,
	Line,
	LineChart,
	ResponsiveContainer,
	Tooltip,
	XAxis,
	YAxis,
} from "recharts";
import axios from "axios";
import { useLocation, useNavigate } from "react-router-dom";
import {
	fetchAdminHealth,
	fetchAdminRuns,
	fetchAdminSchedules,
	fetchAdminStats,
	fetchMatchJobs,
	fetchMatchJobStatus,
	runMatchJob,
	type AdminHealth,
	type JobRun,
	type JobRunStats,
	type JobSchedule,
	type MatchJobListItem,
	type MatchJobStatus,
	type MatchRunRequest,
} from "./api/admin";

type TrendPoint = {
	label: string;
	total: number;
	failed: number;
	failureRate: number;
};

const SHOP_OPTIONS = [
	{ label: "Coles", value: 0 },
	{ label: "Woolworths", value: 1 },
];

const scheduleKey = (schedule: JobSchedule) =>
	`${schedule.source}:${schedule.jobName}`;

const formatDateTime = (value?: string | null) =>
	value ? new Date(value).toLocaleString() : "-";

const formatDuration = (value?: number | null) => {
	if (value === null || value === undefined) return "-";
	if (value < 1000) return `${value} ms`;
	const seconds = value / 1000;
	if (seconds < 60) return `${seconds.toFixed(1)} s`;
	const minutes = seconds / 60;
	return `${minutes.toFixed(1)} min`;
};

const buildTrendData = (
	runs: JobRun[],
	rangeHours: number,
	bucket: "day" | "hour",
): TrendPoint[] => {
	const now = new Date();
	const start = new Date(now.getTime() - rangeHours * 60 * 60 * 1000);
	const cursor = new Date(start);
	if (bucket === "day") {
		cursor.setHours(0, 0, 0, 0);
	} else {
		cursor.setMinutes(0, 0, 0);
	}

	const buckets = new Map<string, TrendPoint>();
	const bucketKey = (date: Date) => {
		if (bucket === "day") {
			return date.toISOString().slice(0, 10);
		}
		return date.toISOString().slice(0, 13);
	};
	const bucketLabel = (date: Date) => {
		if (bucket === "day") {
			return date.toLocaleDateString();
		}
		return date.toLocaleString(undefined, {
			month: "2-digit",
			day: "2-digit",
			hour: "2-digit",
		});
	};

	while (cursor <= now) {
		const key = bucketKey(cursor);
		buckets.set(key, {
			label: bucketLabel(cursor),
			total: 0,
			failed: 0,
			failureRate: 0,
		});

		if (bucket === "day") {
			cursor.setDate(cursor.getDate() + 1);
		} else {
			cursor.setHours(cursor.getHours() + 1);
		}
	}

	for (const run of runs) {
		const startTime = new Date(run.startTime);
		if (startTime < start) continue;
		const key = bucketKey(startTime);
		const entry = buckets.get(key);
		if (!entry) continue;
		entry.total += 1;
		if (run.status === "fail") entry.failed += 1;
	}

	return Array.from(buckets.values()).map((entry) => ({
		...entry,
		failureRate: entry.total === 0 ? 0 : entry.failed / entry.total,
	}));
};

const statusChip = (status?: string | null) => {
	if (!status) {
		return <Chip label="-" size="small" variant="outlined" />;
	}
	if (status === "success") {
		return <Chip label="success" size="small" color="success" />;
	}
	if (status === "fail") {
		return <Chip label="fail" size="small" color="error" />;
	}
	return <Chip label={status} size="small" variant="outlined" />;
};

const shopLabel = (value: number) =>
	SHOP_OPTIONS.find((option) => option.value === value)?.label ?? String(value);

export default function AdminJobsPage() {
	const [schedules, setSchedules] = React.useState<JobSchedule[]>([]);
	const [selectedKey, setSelectedKey] = React.useState<string | null>(null);
	const [runs, setRuns] = React.useState<JobRun[]>([]);
	const [stats, setStats] = React.useState<JobRunStats | null>(null);
	const [health, setHealth] = React.useState<AdminHealth | null>(null);
	const [rangeHours, setRangeHours] = React.useState(24);
	const [trendBucket, setTrendBucket] = React.useState<"day" | "hour">("day");
	const [loadingSchedules, setLoadingSchedules] = React.useState(false);
	const [loadingRuns, setLoadingRuns] = React.useState(false);
	const [loadingStats, setLoadingStats] = React.useState(false);
	const [loadingHealth, setLoadingHealth] = React.useState(false);
	const [error, setError] = React.useState<string | null>(null);
	const [preMatchRequest, setPreMatchRequest] =
		React.useState<MatchRunRequest>({
			sourceShop: 0,
			targetShop: 1,
			mode: "incremental",
			limit: 1000,
			topN: 20,
			useLlm: false,
			force: false,
		});
	const [preMatchJobId, setPreMatchJobId] = React.useState<string | null>(null);
	const [preMatchStatus, setPreMatchStatus] =
		React.useState<MatchJobStatus | null>(null);
	const [preMatchLoading, setPreMatchLoading] = React.useState(false);
	const [preMatchError, setPreMatchError] = React.useState<string | null>(null);
	const [matchJobs, setMatchJobs] = React.useState<MatchJobListItem[]>([]);
	const [matchJobsTotal, setMatchJobsTotal] = React.useState(0);
	const [matchJobsLoading, setMatchJobsLoading] = React.useState(false);
	const [matchJobsError, setMatchJobsError] = React.useState<string | null>(null);
	const [matchJobsFilters, setMatchJobsFilters] = React.useState({
		statuses: [] as string[],
		updatedFrom: "",
		updatedTo: "",
		sortBy: "updatedAt" as "updatedAt" | "createdAt",
		sortDir: "desc" as "asc" | "desc",
	});
	const [matchJobsPagination, setMatchJobsPagination] = React.useState({
		page: 0,
		pageSize: 20,
	});
	const location = useLocation();
	const navigate = useNavigate();
	const adminTab = location.pathname.includes("/prematch")
		? "prematch"
		: "schedules";

	React.useEffect(() => {
		if (location.pathname === "/admin" || location.pathname === "/admin/") {
			navigate("/admin/schedules", { replace: true });
		}
	}, [location.pathname, navigate]);

	const selectedSchedule = React.useMemo(
		() => schedules.find((s) => scheduleKey(s) === selectedKey) ?? null,
		[schedules, selectedKey],
	);
	const selectionModel = React.useMemo<GridRowSelectionModel>(
		() => ({
			type: "include",
			ids: new Set(selectedKey ? [selectedKey] : []),
		}),
		[selectedKey],
	);

	const scheduleColumns: GridColDef<JobSchedule>[] = [
		{ field: "jobName", headerName: "Job", flex: 1, minWidth: 220 },
		{ field: "source", headerName: "Source", width: 100 },
		{
			field: "enabled",
			headerName: "State",
			width: 110,
			renderCell: (params) =>
				params.value ? (
					<Chip label="enabled" size="small" color="success" />
				) : (
					<Chip label="disabled" size="small" color="default" />
				),
		},
		{
			field: "nextFireTimeUtc",
			headerName: "Next run",
			width: 180,
			valueFormatter: (value) => formatDateTime(value as string | null),
		},
		{
			field: "lastRunStatus",
			headerName: "Last status",
			width: 120,
			renderCell: (params) => statusChip(params.value as string | null),
		},
		{
			field: "lastRunTimeUtc",
			headerName: "Last run",
			width: 180,
			valueFormatter: (value) => formatDateTime(value as string | null),
		},
	];

	const runColumns: GridColDef<JobRun>[] = [
		{
			field: "startTime",
			headerName: "Started",
			width: 180,
			valueFormatter: (value) => formatDateTime(value as string),
		},
		{
			field: "status",
			headerName: "Status",
			width: 110,
			renderCell: (params) => statusChip(params.value as string | null),
		},
		{
			field: "durationMs",
			headerName: "Duration",
			width: 120,
			valueFormatter: (value) => formatDuration(value as number | null),
		},
		{
			field: "errorMessage",
			headerName: "Error",
			flex: 1,
			minWidth: 220,
			renderCell: (params) => (
				<Box
					title={params.value ? String(params.value) : ""}
					sx={{
						overflow: "hidden",
						textOverflow: "ellipsis",
						whiteSpace: "nowrap",
						width: "100%",
					}}
				>
					{params.value ? String(params.value) : "-"}
				</Box>
			),
		},
		{ field: "environment", headerName: "Env", width: 110 },
		{ field: "requestId", headerName: "Request ID", width: 220 },
	];

	const matchJobColumns: GridColDef<MatchJobListItem>[] = [
		{ field: "id", headerName: "ID", width: 240 },
		{
			field: "status",
			headerName: "Status",
			width: 120,
			renderCell: (params) => statusChip(params.value as string | null),
		},
		{
			field: "sourceShop",
			headerName: "Source",
			width: 100,
			valueFormatter: (value) => shopLabel(Number(value)),
		},
		{
			field: "targetShop",
			headerName: "Target",
			width: 100,
			valueFormatter: (value) => shopLabel(Number(value)),
		},
		{ field: "mode", headerName: "Mode", width: 130 },
		{
			field: "useLlm",
			headerName: "Use LLM",
			width: 110,
			renderCell: (params) => (
				<Chip
					label={params.value ? "Yes" : "No"}
					size="small"
					variant="outlined"
				/>
			),
		},
		{ field: "total", headerName: "Total", width: 90 },
		{ field: "processed", headerName: "Processed", width: 110 },
		{ field: "matched", headerName: "Matched", width: 100 },
		{ field: "comparable", headerName: "Comparable", width: 120 },
		{ field: "failed", headerName: "Failed", width: 90 },
		{ field: "limitNum", headerName: "Limit", width: 90 },
		{ field: "topN", headerName: "Top N", width: 80 },
		{
			field: "since",
			headerName: "Since",
			width: 180,
			valueFormatter: (value) => formatDateTime(value as string | null),
		},
		{
			field: "createdAt",
			headerName: "Created",
			width: 180,
			valueFormatter: (value) => formatDateTime(value as string | null),
		},
		{
			field: "updatedAt",
			headerName: "Updated",
			width: 180,
			valueFormatter: (value) => formatDateTime(value as string | null),
		},
		{
			field: "errorMessage",
			headerName: "Error",
			flex: 1,
			minWidth: 220,
			renderCell: (params) => (
				<Box
					title={params.value ? String(params.value) : ""}
					sx={{
						overflow: "hidden",
						textOverflow: "ellipsis",
						whiteSpace: "nowrap",
						width: "100%",
					}}
				>
					{params.value ? String(params.value) : "-"}
				</Box>
			),
		},
	];

	const loadSchedules = React.useCallback(async () => {
		setLoadingSchedules(true);
		setError(null);
		try {
			const data = await fetchAdminSchedules();
			setSchedules(data);
			const hasSelection =
				selectedKey &&
				data.some((schedule) => scheduleKey(schedule) === selectedKey);
			if (hasSelection) return;
			setSelectedKey(data.length > 0 ? scheduleKey(data[0]) : null);
		} catch (e) {
			if (axios.isAxiosError(e) && e.response?.status === 403) {
				setError("Not authorized to view admin data.");
			} else {
				setError("Failed to load schedules.");
			}
		} finally {
			setLoadingSchedules(false);
		}
	}, [selectedKey]);

	const loadHealth = React.useCallback(async () => {
		setLoadingHealth(true);
		setError(null);
		try {
			const data = await fetchAdminHealth(rangeHours);
			setHealth(data);
		} catch (e) {
			if (axios.isAxiosError(e) && e.response?.status === 403) {
				setError("Not authorized to view admin data.");
			} else {
				setError("Failed to load health summary.");
			}
		} finally {
			setLoadingHealth(false);
		}
	}, [rangeHours]);

	const loadRunsAndStats = React.useCallback(async () => {
		if (!selectedSchedule) {
			setRuns([]);
			setStats(null);
			return;
		}
		setLoadingRuns(true);
		setLoadingStats(true);
		setError(null);
		try {
			const [runsData, statsData] = await Promise.all([
				fetchAdminRuns(selectedSchedule.jobName, selectedSchedule.source, 500),
				fetchAdminStats(selectedSchedule.jobName, selectedSchedule.source, rangeHours),
			]);
			setRuns(runsData);
			setStats(statsData);
		} catch (e) {
			if (axios.isAxiosError(e) && e.response?.status === 403) {
				setError("Not authorized to view admin data.");
			} else {
				setError("Failed to load runs or stats.");
			}
		} finally {
			setLoadingRuns(false);
			setLoadingStats(false);
		}
	}, [selectedSchedule, rangeHours]);

	React.useEffect(() => {
		void loadSchedules();
	}, [loadSchedules]);

	React.useEffect(() => {
		void loadHealth();
	}, [loadHealth]);

	React.useEffect(() => {
		void loadRunsAndStats();
	}, [loadRunsAndStats]);

	const trendData = React.useMemo(
		() => buildTrendData(runs, rangeHours, trendBucket),
		[runs, rangeHours, trendBucket],
	);

	const runPreMatch = React.useCallback(async () => {
		setPreMatchError(null);
		const resumeJobId = preMatchRequest.resumeJobId?.trim();
		const isResume = Boolean(resumeJobId);
		if (!isResume && preMatchRequest.sourceShop === preMatchRequest.targetShop) {
			setPreMatchError("Source shop and target shop must be different.");
			return;
		}
		setPreMatchLoading(true);
		try {
			const payload: MatchRunRequest = {
				...preMatchRequest,
				resumeJobId: resumeJobId || undefined,
				limit:
					preMatchRequest.limit != null
						? Math.max(0, Number(preMatchRequest.limit))
						: undefined,
				topN:
					preMatchRequest.topN != null
						? Math.max(1, Number(preMatchRequest.topN))
						: undefined,
			};
			const result = await runMatchJob(payload);
			setPreMatchJobId(result.jobId);
			const status = await fetchMatchJobStatus(result.jobId);
			setPreMatchStatus(status);
		} catch (e) {
			if (axios.isAxiosError(e) && e.response?.status === 403) {
				setPreMatchError("Not authorized to run pre-match.");
			} else {
				setPreMatchError("Failed to start pre-match job.");
			}
		} finally {
			setPreMatchLoading(false);
		}
	}, [preMatchRequest]);

	const loadMatchJobs = React.useCallback(async () => {
		setMatchJobsError(null);
		setMatchJobsLoading(true);
		try {
			const result = await fetchMatchJobs({
				status: matchJobsFilters.statuses.length
					? matchJobsFilters.statuses.join(",")
					: undefined,
				updatedFrom: matchJobsFilters.updatedFrom || undefined,
				updatedTo: matchJobsFilters.updatedTo || undefined,
				page: matchJobsPagination.page + 1,
				pageSize: matchJobsPagination.pageSize,
				sortBy: matchJobsFilters.sortBy,
				sortDir: matchJobsFilters.sortDir,
			});
			setMatchJobs(result.items);
			setMatchJobsTotal(result.total);
		} catch (e) {
			if (axios.isAxiosError(e) && e.response?.status === 403) {
				setMatchJobsError("Not authorized to view match jobs.");
			} else {
				setMatchJobsError("Failed to load match jobs.");
			}
		} finally {
			setMatchJobsLoading(false);
		}
	}, [matchJobsPagination.page, matchJobsPagination.pageSize, matchJobsFilters]);

	React.useEffect(() => {
		if (adminTab !== "prematch") return;
		void loadMatchJobs();
	}, [adminTab, loadMatchJobs]);

	return (
		<Box>
			<Stack spacing={3}>
				<Stack
					direction={{ xs: "column", md: "row" }}
					alignItems={{ xs: "flex-start", md: "center" }}
					justifyContent="space-between"
					spacing={1.5}
				>
					<Typography variant="h4" fontWeight={800}>
						Admin Monitoring
					</Typography>
					{adminTab === "schedules" && (
						<Button variant="contained" onClick={loadSchedules}>
							Refresh schedules
						</Button>
					)}
				</Stack>

				{error && <Alert severity="error">{error}</Alert>}

				<Tabs
					value={adminTab}
					onChange={(_, value) => {
						const next =
							value === "prematch" ? "/admin/prematch" : "/admin/schedules";
						navigate(next);
					}}
					textColor="primary"
					indicatorColor="primary"
				>
					<Tab value="schedules" label="Schedules" />
					<Tab value="prematch" label="Pre-match" />
				</Tabs>

				{adminTab === "schedules" && (
					<>
						<Card variant="outlined">
					<CardContent>
						<Stack
							direction={{ xs: "column", md: "row" }}
							spacing={2}
							alignItems={{ xs: "flex-start", md: "center" }}
						>
							<Typography variant="h6" fontWeight={700}>
								Health summary
							</Typography>
							<FormControl size="small" sx={{ minWidth: 160 }}>
								<InputLabel id="range-hours-label">Range</InputLabel>
								<Select
									labelId="range-hours-label"
									label="Range"
									value={rangeHours}
									onChange={(e) => setRangeHours(Number(e.target.value))}
								>
									<MenuItem value={24}>Last 24h</MenuItem>
									<MenuItem value={168}>Last 7d</MenuItem>
									<MenuItem value={720}>Last 30d</MenuItem>
								</Select>
							</FormControl>
							<Button variant="outlined" onClick={loadHealth}>
								Refresh summary
							</Button>
						</Stack>

						<Divider sx={{ my: 2 }} />

						{loadingHealth ? (
							<Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
								<CircularProgress size={20} />
								<Typography variant="body2">Loading health...</Typography>
							</Box>
						) : (
							<Stack
								direction={{ xs: "column", md: "row" }}
								spacing={2}
								flexWrap="wrap"
							>
								<Stack spacing={0.5}>
									<Typography variant="subtitle2" color="text.secondary">
										Total runs
									</Typography>
									<Typography variant="h6" fontWeight={700}>
										{health?.totalRuns ?? 0}
									</Typography>
								</Stack>
								<Stack spacing={0.5}>
									<Typography variant="subtitle2" color="text.secondary">
										Failed runs
									</Typography>
									<Typography variant="h6" fontWeight={700}>
										{health?.failedRuns ?? 0}
									</Typography>
								</Stack>
								<Stack spacing={0.5}>
									<Typography variant="subtitle2" color="text.secondary">
										Failure rate
									</Typography>
									<Typography variant="h6" fontWeight={700}>
										{health ? `${Math.round(health.failureRate * 100)}%` : "0%"}
									</Typography>
								</Stack>
								<Stack spacing={0.5}>
									<Typography variant="subtitle2" color="text.secondary">
										Last failure
									</Typography>
									<Typography variant="h6" fontWeight={700}>
										{health?.lastFailureAtUtc
											? formatDateTime(health.lastFailureAtUtc)
											: "-"}
									</Typography>
								</Stack>
							</Stack>
						)}

						{health?.recentFailures?.length ? (
							<Box sx={{ mt: 2 }}>
								<Typography variant="subtitle2" color="text.secondary">
									Recent failures
								</Typography>
								<Stack spacing={1} sx={{ mt: 1 }}>
									{health.recentFailures.map((failure) => (
										<Box key={`${failure.source}:${failure.jobName}:${failure.startTimeUtc}`}>
											<Typography variant="body2" fontWeight={600}>
												{failure.jobName} ({failure.source})
											</Typography>
											<Typography variant="caption" color="text.secondary">
												{formatDateTime(failure.startTimeUtc)}{" "}
												{failure.errorMessage ? `- ${failure.errorMessage}` : ""}
											</Typography>
										</Box>
									))}
								</Stack>
							</Box>
						) : null}
					</CardContent>
						</Card>
					</>
				)}

				{adminTab === "prematch" && (
					<Stack spacing={2}>
						<Card variant="outlined">
						<CardContent>
							<Stack
								direction={{ xs: "column", md: "row" }}
								spacing={2}
								alignItems={{ xs: "flex-start", md: "center" }}
								justifyContent="space-between"
							>
								<Typography variant="h6" fontWeight={700}>
									Batch pre-match
								</Typography>
								<Stack direction="row" spacing={1}>
									<Button
										variant="contained"
										onClick={runPreMatch}
										disabled={preMatchLoading}
									>
										{preMatchLoading ? "Starting..." : "Run pre-match"}
									</Button>
								</Stack>
							</Stack>

							<Divider sx={{ my: 2 }} />

							<Box sx={{ overflowX: "auto", pt: 1.5, pb: 0.5 }}>
								<Stack
									direction="row"
									spacing={2}
									alignItems="center"
									flexWrap="nowrap"
									sx={{ minWidth: "max-content" }}
								>
								<FormControl size="small" sx={{ minWidth: 160 }}>
									<InputLabel id="pre-match-source-label">Source shop</InputLabel>
									<Select
										labelId="pre-match-source-label"
										label="Source shop"
										value={preMatchRequest.sourceShop}
										onChange={(e) =>
											setPreMatchRequest((prev) => ({
												...prev,
												sourceShop: Number(e.target.value),
											}))
										}
									>
										{SHOP_OPTIONS.map((option) => (
											<MenuItem key={option.value} value={option.value}>
												{option.label}
											</MenuItem>
										))}
									</Select>
								</FormControl>
								<FormControl size="small" sx={{ minWidth: 160 }}>
									<InputLabel id="pre-match-target-label">Target shop</InputLabel>
									<Select
										labelId="pre-match-target-label"
										label="Target shop"
										value={preMatchRequest.targetShop}
										onChange={(e) =>
											setPreMatchRequest((prev) => ({
												...prev,
												targetShop: Number(e.target.value),
											}))
										}
									>
										{SHOP_OPTIONS.map((option) => (
											<MenuItem key={option.value} value={option.value}>
												{option.label}
											</MenuItem>
										))}
									</Select>
								</FormControl>
								<FormControl size="small" sx={{ minWidth: 160 }}>
									<InputLabel id="pre-match-mode-label">Mode</InputLabel>
									<Select
										labelId="pre-match-mode-label"
										label="Mode"
										value={preMatchRequest.mode ?? "incremental"}
										onChange={(e) =>
											setPreMatchRequest((prev) => ({
												...prev,
												mode: String(e.target.value),
											}))
										}
									>
										<MenuItem value="incremental">Incremental</MenuItem>
										<MenuItem value="full">Full</MenuItem>
									</Select>
								</FormControl>
								<FormControl size="small" sx={{ minWidth: 220 }}>
									<InputLabel id="pre-match-resume-label" shrink>
										Resume Job ID
									</InputLabel>
									<OutlinedInput
										label="Resume Job ID"
										placeholder="Optional"
										value={preMatchRequest.resumeJobId ?? ""}
										onChange={(e) =>
											setPreMatchRequest((prev) => ({
												...prev,
												resumeJobId: e.target.value,
											}))
										}
										notched
									/>
								</FormControl>
								<TextField
									label="Limit"
									size="small"
									type="number"
									value={preMatchRequest.limit ?? 0}
									onChange={(e) =>
										setPreMatchRequest((prev) => ({
											...prev,
											limit: Number(e.target.value),
										}))
									}
									inputProps={{ min: 0 }}
									sx={{ width: 120 }}
								/>
								<TextField
									label="Top N"
									size="small"
									type="number"
									value={preMatchRequest.topN ?? 20}
									onChange={(e) =>
										setPreMatchRequest((prev) => ({
											...prev,
											topN: Number(e.target.value),
										}))
									}
									inputProps={{ min: 1, max: 50 }}
									sx={{ width: 100 }}
								/>
								<FormControl size="small" sx={{ minWidth: 140 }}>
									<InputLabel id="pre-match-llm-label">Use LLM</InputLabel>
									<Select
										labelId="pre-match-llm-label"
										label="Use LLM"
										value={preMatchRequest.useLlm ? "yes" : "no"}
										onChange={(e) =>
											setPreMatchRequest((prev) => ({
												...prev,
												useLlm: e.target.value === "yes",
											}))
										}
									>
										<MenuItem value="no">No</MenuItem>
										<MenuItem value="yes">Yes</MenuItem>
									</Select>
								</FormControl>
								<FormControl size="small" sx={{ minWidth: 160 }}>
									<InputLabel id="pre-match-force-label">
										Force re-match
									</InputLabel>
									<Select
										labelId="pre-match-force-label"
										label="Force re-match"
										value={preMatchRequest.force ? "yes" : "no"}
										onChange={(e) =>
											setPreMatchRequest((prev) => ({
												...prev,
												force: e.target.value === "yes",
											}))
										}
									>
										<MenuItem value="no">No</MenuItem>
										<MenuItem value="yes">Yes</MenuItem>
									</Select>
								</FormControl>
								</Stack>
							</Box>

							{preMatchError && (
								<Alert severity="error" sx={{ mt: 2 }}>
									{preMatchError}
								</Alert>
							)}

							{preMatchJobId && (
								<Box sx={{ mt: 2 }}>
									<Typography variant="subtitle2" color="text.secondary">
										Job ID
									</Typography>
									<Typography variant="body2">{preMatchJobId}</Typography>
								</Box>
							)}

							{preMatchStatus && (
								<Box sx={{ mt: 2 }}>
									<Typography variant="subtitle2" color="text.secondary">
										Status
									</Typography>
									<Stack spacing={0.5}>
										<Typography variant="body2">
											State: {preMatchStatus.status}
										</Typography>
										<Typography variant="body2">
											{shopLabel(preMatchStatus.sourceShop)} →{" "}
											{shopLabel(preMatchStatus.targetShop)}
										</Typography>
										<Typography variant="body2">
											Mode: {preMatchStatus.mode}, Top N: {preMatchStatus.topN},
											{" "}Limit: {preMatchStatus.limitNum}
										</Typography>
										<Typography variant="body2">
											Use LLM: {preMatchStatus.useLlm ? "Yes" : "No"}
										</Typography>
										<Typography variant="body2">
											Progress: {preMatchStatus.processed}/{preMatchStatus.total}
										</Typography>
										<Typography variant="body2">
											Matched: {preMatchStatus.matched}, Comparable:{" "}
											{preMatchStatus.comparable}, Failed:{" "}
											{preMatchStatus.failed}
										</Typography>
										<Typography variant="body2">
											Updated: {formatDateTime(preMatchStatus.updatedAt)}
										</Typography>
										{preMatchStatus.errorMessage ? (
											<Typography variant="body2" color="error">
												Error: {preMatchStatus.errorMessage}
											</Typography>
										) : null}
									</Stack>
								</Box>
							)}
						</CardContent>
						</Card>

						<Card variant="outlined">
						<CardContent>
							<Stack
								direction={{ xs: "column", md: "row" }}
								spacing={2}
								alignItems={{ xs: "flex-start", md: "center" }}
								justifyContent="space-between"
							>
								<Typography variant="h6" fontWeight={700}>
									Match jobs
								</Typography>
							</Stack>

							<Divider sx={{ my: 2 }} />

							<Stack
								direction={{ xs: "column", md: "row" }}
								spacing={2}
								alignItems={{ xs: "stretch", md: "center" }}
								flexWrap="wrap"
							>
								<FormControl size="small" sx={{ minWidth: 220 }}>
									<InputLabel id="match-jobs-status-label" shrink>
										Status
									</InputLabel>
									<Select
										labelId="match-jobs-status-label"
										label="Status"
										multiple
										displayEmpty
										value={matchJobsFilters.statuses}
										onChange={(e) => {
											const value = e.target.value;
											setMatchJobsPagination((prev) => ({
												...prev,
												page: 0,
											}));
											setMatchJobsFilters((prev) => ({
												...prev,
												statuses:
													typeof value === "string" ? value.split(",") : value,
											}));
										}}
										input={<OutlinedInput label="Status" notched />}
										renderValue={(selected) => {
											const list = selected as string[];
											if (list.length === 0) {
												return (
													<Typography color="text.disabled">
														Optional
													</Typography>
												);
											}
											return list.join(", ");
										}}
									>
										{["queued", "running", "completed", "failed"].map((status) => (
											<MenuItem key={status} value={status}>
												<Checkbox
													checked={matchJobsFilters.statuses.includes(status)}
												/>
												<ListItemText primary={status} />
											</MenuItem>
										))}
									</Select>
								</FormControl>
								<TextField
									label="Updated from"
									size="small"
									type="datetime-local"
									InputLabelProps={{ shrink: true }}
									value={matchJobsFilters.updatedFrom}
									onChange={(e) =>
										{
											setMatchJobsPagination((prev) => ({
												...prev,
												page: 0,
											}));
											setMatchJobsFilters((prev) => ({
												...prev,
												updatedFrom: e.target.value,
											}));
										}
									}
									sx={{
										minWidth: 220,
										"& input": {
											color: matchJobsFilters.updatedFrom
												? "text.primary"
												: "text.disabled",
										},
										"& input::-webkit-datetime-edit": {
											color: matchJobsFilters.updatedFrom
												? "inherit"
												: "text.disabled",
										},
									}}
								/>
								<TextField
									label="Updated to"
									size="small"
									type="datetime-local"
									InputLabelProps={{ shrink: true }}
									value={matchJobsFilters.updatedTo}
									onChange={(e) =>
										{
											setMatchJobsPagination((prev) => ({
												...prev,
												page: 0,
											}));
											setMatchJobsFilters((prev) => ({
												...prev,
												updatedTo: e.target.value,
											}));
										}
									}
									sx={{
										minWidth: 220,
										"& input": {
											color: matchJobsFilters.updatedTo
												? "text.primary"
												: "text.disabled",
										},
										"& input::-webkit-datetime-edit": {
											color: matchJobsFilters.updatedTo
												? "inherit"
												: "text.disabled",
										},
									}}
								/>
								<FormControl size="small" sx={{ minWidth: 160 }}>
									<InputLabel id="match-jobs-sortby-label">Sort by</InputLabel>
									<Select
										labelId="match-jobs-sortby-label"
										label="Sort by"
										value={matchJobsFilters.sortBy}
										onChange={(e) =>
											{
												setMatchJobsPagination((prev) => ({
													...prev,
													page: 0,
												}));
												setMatchJobsFilters((prev) => ({
													...prev,
													sortBy: e.target.value as "updatedAt" | "createdAt",
												}));
											}
										}
									>
										<MenuItem value="updatedAt">Updated</MenuItem>
										<MenuItem value="createdAt">Created</MenuItem>
									</Select>
								</FormControl>
								<FormControl size="small" sx={{ minWidth: 140 }}>
									<InputLabel id="match-jobs-sortdir-label">
										Direction
									</InputLabel>
									<Select
										labelId="match-jobs-sortdir-label"
										label="Direction"
										value={matchJobsFilters.sortDir}
										onChange={(e) =>
											{
												setMatchJobsPagination((prev) => ({
													...prev,
													page: 0,
												}));
												setMatchJobsFilters((prev) => ({
													...prev,
													sortDir: e.target.value as "asc" | "desc",
												}));
											}
										}
									>
										<MenuItem value="desc">Desc</MenuItem>
										<MenuItem value="asc">Asc</MenuItem>
									</Select>
								</FormControl>
							</Stack>

							{matchJobsError && (
								<Alert severity="error" sx={{ mt: 2 }}>
									{matchJobsError}
								</Alert>
							)}

							<Box sx={{ mt: 2 }}>
								<DataGrid
									rows={matchJobs}
									columns={matchJobColumns}
									getRowId={(row) => row.id}
									autoHeight
									loading={matchJobsLoading}
									rowCount={matchJobsTotal}
									paginationMode="server"
									paginationModel={matchJobsPagination}
									onPaginationModelChange={(model) => {
										setMatchJobsPagination(model);
									}}
									pageSizeOptions={[10, 20, 50]}
									disableRowSelectionOnClick
								/>
							</Box>
						</CardContent>
						</Card>
					</Stack>
				)}

				{adminTab === "schedules" && (
					<>
						<Card variant="outlined">
					<CardContent>
						<Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>
							Schedules
						</Typography>
						<DataGrid
							rows={schedules}
							columns={scheduleColumns}
							getRowId={(row) => scheduleKey(row)}
							autoHeight
							loading={loadingSchedules}
							pageSizeOptions={[10, 25, 50]}
							initialState={{ pagination: { paginationModel: { pageSize: 10, page: 0 } } }}
							rowSelectionModel={selectionModel}
							onRowSelectionModelChange={(model) => {
								const nextId =
									model.ids.size > 0
										? Array.from(model.ids)[0]
										: null;
								const nextKey = nextId ? String(nextId) : null;
								setSelectedKey(nextKey);
							}}
						/>
					</CardContent>
						</Card>

						<Stack direction={{ xs: "column", md: "row" }} spacing={2}>
					<Card variant="outlined" sx={{ flex: 1 }}>
						<CardContent>
							<Typography variant="h6" fontWeight={700} sx={{ mb: 1 }}>
								Selected job
							</Typography>
							{selectedSchedule ? (
								<Stack spacing={1}>
									<Typography variant="subtitle2" color="text.secondary">
										{selectedSchedule.jobName} ({selectedSchedule.source})
									</Typography>
									<Typography variant="body2">
										Schedule: {selectedSchedule.scheduleExpression || "-"}
									</Typography>
									<Typography variant="body2">
										Timezone: {selectedSchedule.timezone}
									</Typography>
									<Typography variant="body2">
										Next run: {formatDateTime(selectedSchedule.nextFireTimeUtc || null)}
									</Typography>
									<Typography variant="body2">
										Last run: {formatDateTime(selectedSchedule.lastRunTimeUtc || null)}
									</Typography>
									<Typography variant="body2">
										Status: {selectedSchedule.lastRunStatus || "-"}
									</Typography>
									<Typography variant="body2">
										Last error: {selectedSchedule.lastRunErrorMessage || "-"}
									</Typography>
								</Stack>
							) : (
								<Typography variant="body2" color="text.secondary">
									Select a schedule to view details.
								</Typography>
							)}
						</CardContent>
					</Card>

					<Card variant="outlined" sx={{ flex: 1 }}>
						<CardContent>
							<Stack
								direction="row"
								alignItems="center"
								justifyContent="space-between"
								sx={{ mb: 1 }}
							>
								<Typography variant="h6" fontWeight={700}>
									Trend
								</Typography>
								<FormControl size="small" sx={{ minWidth: 120 }}>
									<InputLabel id="trend-bucket-label">Bucket</InputLabel>
									<Select
										labelId="trend-bucket-label"
										label="Bucket"
										value={trendBucket}
										onChange={(e) =>
											setTrendBucket(e.target.value as "day" | "hour")
										}
									>
										<MenuItem value="day">Daily</MenuItem>
										<MenuItem value="hour">Hourly</MenuItem>
									</Select>
								</FormControl>
							</Stack>
							<ResponsiveContainer width="100%" height={260}>
								<LineChart data={trendData}>
									<CartesianGrid strokeDasharray="3 3" />
									<XAxis dataKey="label" tick={{ fontSize: 11 }} />
									<YAxis
										tickFormatter={(value) => `${Math.round(value * 100)}%`}
										domain={[0, 1]}
										tick={{ fontSize: 11 }}
									/>
									<Tooltip
										formatter={(value) =>
											`${Math.round(Number(value) * 100)}%`
										}
									/>
									<Line
										type="monotone"
										dataKey="failureRate"
										stroke="#ef4444"
										strokeWidth={2}
										dot={false}
									/>
								</LineChart>
							</ResponsiveContainer>
						</CardContent>
					</Card>
						</Stack>

						<Card variant="outlined">
					<CardContent>
						<Typography variant="h6" fontWeight={700} sx={{ mb: 2 }}>
							Recent runs
						</Typography>
						{loadingRuns || loadingStats ? (
							<Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
								<CircularProgress size={20} />
								<Typography variant="body2">Loading runs...</Typography>
							</Box>
						) : (
							<DataGrid
								rows={runs}
								columns={runColumns}
								getRowId={(row) => row.id}
								autoHeight
								pageSizeOptions={[10, 25, 50]}
								initialState={{
									pagination: { paginationModel: { pageSize: 10, page: 0 } },
								}}
								disableRowSelectionOnClick
							/>
						)}
						{stats && (
							<Box sx={{ mt: 2 }}>
								<Divider sx={{ mb: 2 }} />
								<Stack direction={{ xs: "column", md: "row" }} spacing={2}>
									<Stack spacing={0.5}>
										<Typography variant="subtitle2" color="text.secondary">
											Total
										</Typography>
										<Typography variant="h6" fontWeight={700}>
											{stats.totalRuns}
										</Typography>
									</Stack>
									<Stack spacing={0.5}>
										<Typography variant="subtitle2" color="text.secondary">
											Failed
										</Typography>
										<Typography variant="h6" fontWeight={700}>
											{stats.failedRuns}
										</Typography>
									</Stack>
									<Stack spacing={0.5}>
										<Typography variant="subtitle2" color="text.secondary">
											Failure rate
										</Typography>
										<Typography variant="h6" fontWeight={700}>
											{Math.round(stats.failureRate * 100)}%
										</Typography>
									</Stack>
									<Stack spacing={0.5}>
										<Typography variant="subtitle2" color="text.secondary">
											Avg duration
										</Typography>
										<Typography variant="h6" fontWeight={700}>
											{formatDuration(stats.averageDurationMs)}
										</Typography>
									</Stack>
								</Stack>
							</Box>
						)}
					</CardContent>
						</Card>
					</>
				)}
			</Stack>
		</Box>
	);
}
