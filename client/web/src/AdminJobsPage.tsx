import * as React from "react";
import {
	Alert,
	Avatar,
	Box,
	Button,
	ButtonBase,
	Checkbox,
	Chip,
	CircularProgress,
	Collapse,
	FormControl,
	FormControlLabel,
	IconButton,
	InputLabel,
	LinearProgress,
	ListItemText,
	MenuItem,
	OutlinedInput,
	Pagination,
	Select,
	Switch,
	TextField,
	Typography,
	useMediaQuery,
	useTheme,
} from "@mui/material";
import RefreshIcon from "@mui/icons-material/Refresh";
import SwapHorizIcon from "@mui/icons-material/SwapHoriz";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import FilterListIcon from "@mui/icons-material/FilterList";
import { DataGrid, type GridColDef, type GridRowSelectionModel } from "@mui/x-data-grid";
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
	fetchProductMatchReview,
	runMatchJob,
	runCleanPriceHistory,
	updateProductMatch,
	type AdminHealth,
	type JobRun,
	type JobRunStats,
	type JobSchedule,
	type MatchJobListItem,
	type MatchJobStatus,
	type MatchRunRequest,
	type ProductMatchReviewItem,
} from "./api/admin";
import SegmentedControl from "./components/SegmentedControl";
import ConfirmSheet from "./components/ConfirmSheet";
import StoreLogo from "./components/StoreLogo";
import { DISPLAY_FONT, tokens } from "./theme";

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
	const [reviewItems, setReviewItems] = React.useState<ProductMatchReviewItem[]>([]);
	const [reviewTotal, setReviewTotal] = React.useState(0);
	const [reviewLoading, setReviewLoading] = React.useState(false);
	const [reviewError, setReviewError] = React.useState<string | null>(null);
	const [reviewPagination, setReviewPagination] = React.useState({
		page: 0,
		pageSize: 20,
	});
	const [reviewEdits, setReviewEdits] = React.useState<
		Record<string, { matchType: "same_product" | "comparable"; score: string }>
	>({});
	const [reviewSavingIds, setReviewSavingIds] = React.useState<string[]>([]);
	const [cleanLoading, setCleanLoading] = React.useState(false);
	const [cleanResult, setCleanResult] = React.useState<number | null>(null);
	const [cleanError, setCleanError] = React.useState<string | null>(null);

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
	const reviewScoreMin = 0.5;
	const reviewScoreMax = 0.9;

	const getReviewEdit = (row: ProductMatchReviewItem) => {
		return (
			reviewEdits[row.matchId] ?? {
				matchType:
					row.matchType === "same_product" ? "same_product" : "comparable",
				score: row.score.toFixed(4),
			}
		);
	};

	const setReviewEdit = (
		row: ProductMatchReviewItem,
		patch: Partial<{ matchType: "same_product" | "comparable"; score: string }>,
	) => {
		setReviewEdits((prev) => {
			const existing =
				prev[row.matchId] ??
				({
					matchType:
						row.matchType === "same_product" ? "same_product" : "comparable",
					score: row.score.toFixed(4),
				} as const);
			return {
				...prev,
				[row.matchId]: {
					...existing,
					...patch,
				},
			};
		});
	};

	const runColumns: GridColDef<JobRun>[] = [
		{
			field: "startTime",
			headerName: "Started",
			width: 180,
			valueFormatter: (value) => shortDateTime(value as string),
		},
		{
			field: "status",
			headerName: "Status",
			width: 110,
			renderCell: (params) => <StatusPill status={params.value as string | null} />,
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
			renderCell: (params) => <StatusPill status={params.value as string | null} />,
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
			valueFormatter: (value) => shortDateTime(value as string | null),
		},
		{
			field: "createdAt",
			headerName: "Created",
			width: 180,
			valueFormatter: (value) => shortDateTime(value as string | null),
		},
		{
			field: "updatedAt",
			headerName: "Updated",
			width: 180,
			valueFormatter: (value) => shortDateTime(value as string | null),
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

	const loadReviewMatches = React.useCallback(async () => {
		setReviewError(null);
		setReviewLoading(true);
		try {
			const result = await fetchProductMatchReview({
				minScore: reviewScoreMin,
				maxScore: reviewScoreMax,
				page: reviewPagination.page + 1,
				pageSize: reviewPagination.pageSize,
			});
			setReviewItems(result.items);
			setReviewTotal(result.total);
		} catch (e) {
			if (axios.isAxiosError(e) && e.response?.status === 403) {
				setReviewError("Not authorized to view match review.");
			} else {
				setReviewError("Failed to load match review.");
			}
		} finally {
			setReviewLoading(false);
		}
	}, [reviewPagination.page, reviewPagination.pageSize, reviewScoreMin, reviewScoreMax]);

	React.useEffect(() => {
		if (adminTab !== "prematch") return;
		void loadReviewMatches();
	}, [adminTab, loadReviewMatches]);

	const saveReview = (row: ProductMatchReviewItem) => {
		const edit = getReviewEdit(row);
		const nextType = edit.matchType;
		const nextScore = Number(edit.score);
		if (nextType === "comparable") {
			if (!Number.isFinite(nextScore)) {
				setReviewError("Score must be a number.");
				return;
			}
			if (nextScore < 0.5 || nextScore > 0.9999) {
				setReviewError("Score must be between 0.5000 and 0.9999.");
				return;
			}
		}
		setReviewSavingIds((prev) => (prev.includes(row.matchId) ? prev : [...prev, row.matchId]));
		setReviewError(null);
		void updateProductMatch({
			matchId: row.matchId,
			matchType: nextType,
			score: nextType === "comparable" ? nextScore : undefined,
		})
			.then(() => loadReviewMatches())
			.catch((e) => {
				if (axios.isAxiosError(e) && e.response?.status === 403) {
					setReviewError("Not authorized to update match.");
				} else {
					setReviewError("Failed to update match.");
				}
			})
			.finally(() => {
				setReviewSavingIds((prev) => prev.filter((id) => id !== row.matchId));
			});
	};

	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));
	const [optionsOpen, setOptionsOpen] = React.useState(false);
	const [filtersOpen, setFiltersOpen] = React.useState(false);
	const [schedPaging, setSchedPaging] = React.useState({ page: 0, pageSize: 10 });
	const [runsPaging, setRunsPaging] = React.useState({ page: 0, pageSize: 10 });
	const [cleanConfirmOpen, setCleanConfirmOpen] = React.useState(false);
	const [statusRefreshing, setStatusRefreshing] = React.useState(false);

	React.useEffect(() => {
		setRunsPaging((prev) => ({ ...prev, page: 0 }));
	}, [selectedKey]);

	const refreshAll = () => {
		void loadSchedules();
		void loadHealth();
		void loadRunsAndStats();
	};

	const runCleanNow = () => {
		setCleanConfirmOpen(false);
		setCleanResult(null);
		setCleanError(null);
		setCleanLoading(true);
		void runCleanPriceHistory()
			.then((res) => setCleanResult(res.deleted))
			.catch(() => setCleanError("Failed to clean price history."))
			.finally(() => setCleanLoading(false));
	};

	const refreshPreMatchStatus = async () => {
		if (!preMatchJobId) return;
		setStatusRefreshing(true);
		try {
			setPreMatchStatus(await fetchMatchJobStatus(preMatchJobId));
		} catch {
			setPreMatchError("Failed to refresh the job status.");
		} finally {
			setStatusRefreshing(false);
		}
	};

	const jobStatus = (s: JobSchedule) => (s.enabled ? s.lastRunStatus || "never run" : "disabled");
	// Oldest → newest, so the strip reads left to right like a timeline.
	const runStrip = runs.slice(0, 20).reverse();
	const preMatchProgress =
		preMatchStatus && preMatchStatus.total > 0
			? Math.min(100, Math.round((preMatchStatus.processed / preMatchStatus.total) * 100))
			: 0;

	const shopBox = (shop: number) => (
		<Box
			sx={{
				flex: 1,
				minWidth: 0,
				height: 44,
				px: 1.25,
				border: `1px solid ${tokens.line}`,
				borderRadius: "12px",
				display: "flex",
				alignItems: "center",
				gap: 1,
				fontSize: 14,
				fontWeight: 600,
			}}
		>
			<StoreLogo shopType={shop} size={22} decorative />
			{shopLabel(shop)}
		</Box>
	);

	const preMatchOptions = (
		<Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
			<Box sx={{ display: "grid", gridTemplateColumns: "repeat(2, minmax(0, 1fr))", gap: 1.5 }}>
				<TextField
					select
					label="Mode"
					size="small"
					value={preMatchRequest.mode ?? "incremental"}
					onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, mode: String(e.target.value) }))}
					sx={fieldSx}
				>
					<MenuItem value="incremental">Incremental</MenuItem>
					<MenuItem value="full">Full</MenuItem>
				</TextField>
				<TextField
					label="Limit"
					size="small"
					type="number"
					value={preMatchRequest.limit ?? 0}
					onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, limit: Number(e.target.value) }))}
					inputProps={{ min: 0 }}
					sx={fieldSx}
				/>
				<TextField
					label="Top N"
					size="small"
					type="number"
					value={preMatchRequest.topN ?? 20}
					onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, topN: Number(e.target.value) }))}
					inputProps={{ min: 1, max: 50 }}
					sx={fieldSx}
				/>
				<TextField
					label="Resume job ID"
					size="small"
					placeholder="Optional"
					value={preMatchRequest.resumeJobId ?? ""}
					onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, resumeJobId: e.target.value }))}
					InputLabelProps={{ shrink: true }}
					sx={fieldSx}
				/>
			</Box>
			<Box sx={{ borderTop: `1px solid ${tokens.line}` }}>
				<FormControlLabel
					labelPlacement="start"
					control={
						<Switch
							checked={Boolean(preMatchRequest.useLlm)}
							onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, useLlm: e.target.checked }))}
						/>
					}
					label="Use LLM"
					sx={switchRowSx}
				/>
				<FormControlLabel
					labelPlacement="start"
					control={
						<Switch
							checked={Boolean(preMatchRequest.force)}
							onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, force: e.target.checked }))}
						/>
					}
					label="Force re-match"
					sx={{ ...switchRowSx, borderBottom: "none" }}
				/>
			</Box>
		</Box>
	);

	const matchJobFilters = (
		<Box
			sx={{
				display: "grid",
				gridTemplateColumns: { xs: "repeat(2, minmax(0, 1fr))", md: "repeat(5, minmax(0, 1fr))" },
				gap: 1.5,
			}}
		>
			<FormControl size="small" sx={{ ...fieldSx, gridColumn: { xs: "span 2", md: "auto" } }}>
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
						setMatchJobsPagination((prev) => ({ ...prev, page: 0 }));
						setMatchJobsFilters((prev) => ({
							...prev,
							statuses: typeof value === "string" ? value.split(",") : value,
						}));
					}}
					input={<OutlinedInput label="Status" notched />}
					renderValue={(selected) => {
						const list = selected as string[];
						if (list.length === 0) {
							return <Typography color="text.secondary">Any</Typography>;
						}
						return list.join(", ");
					}}
				>
					{["queued", "running", "completed", "failed"].map((status) => (
						<MenuItem key={status} value={status}>
							<Checkbox checked={matchJobsFilters.statuses.includes(status)} />
							<ListItemText primary={status} />
						</MenuItem>
					))}
				</Select>
			</FormControl>
			{(["updatedFrom", "updatedTo"] as const).map((key) => (
				<TextField
					key={key}
					label={key === "updatedFrom" ? "Updated from" : "Updated to"}
					size="small"
					type="datetime-local"
					InputLabelProps={{ shrink: true }}
					value={matchJobsFilters[key]}
					onChange={(e) => {
						setMatchJobsPagination((prev) => ({ ...prev, page: 0 }));
						setMatchJobsFilters((prev) => ({ ...prev, [key]: e.target.value }));
					}}
					sx={{ ...fieldSx, gridColumn: { xs: "span 2", md: "auto" } }}
				/>
			))}
			<TextField
				select
				label="Sort by"
				size="small"
				value={matchJobsFilters.sortBy}
				onChange={(e) => {
					setMatchJobsPagination((prev) => ({ ...prev, page: 0 }));
					setMatchJobsFilters((prev) => ({ ...prev, sortBy: e.target.value as "updatedAt" | "createdAt" }));
				}}
				sx={fieldSx}
			>
				<MenuItem value="updatedAt">Updated</MenuItem>
				<MenuItem value="createdAt">Created</MenuItem>
			</TextField>
			<TextField
				select
				label="Direction"
				size="small"
				value={matchJobsFilters.sortDir}
				onChange={(e) => {
					setMatchJobsPagination((prev) => ({ ...prev, page: 0 }));
					setMatchJobsFilters((prev) => ({ ...prev, sortDir: e.target.value as "asc" | "desc" }));
				}}
				sx={fieldSx}
			>
				<MenuItem value="desc">Newest first</MenuItem>
				<MenuItem value="asc">Oldest first</MenuItem>
			</TextField>
		</Box>
	);

	// Desktop (sm+) keeps the data tables: readability and paging first (user feedback 2026-11-19).
	const scheduleSelection = React.useMemo<GridRowSelectionModel>(
		() => ({ type: "include", ids: new Set(selectedKey ? [selectedKey] : []) }),
		[selectedKey],
	);

	const scheduleColumns: GridColDef<JobSchedule>[] = [
		{ field: "jobName", headerName: "Job", flex: 2, minWidth: 300 },
		{ field: "source", headerName: "Source", width: 90 },
		{
			field: "enabled",
			headerName: "State",
			width: 100,
			renderCell: (params) => <StatusPill status={params.value ? "enabled" : "disabled"} />,
		},
		{
			field: "nextFireTimeUtc",
			headerName: "Next run",
			width: 170,
			valueFormatter: (value) => shortDateTime(value as string | null),
		},
		{
			field: "lastRunStatus",
			headerName: "Last status",
			width: 110,
			renderCell: (params) => <StatusPill status={params.value as string | null} />,
		},
		{
			field: "lastRunTimeUtc",
			headerName: "Last run",
			width: 170,
			valueFormatter: (value) => shortDateTime(value as string | null),
		},
		{
			field: "lastRunDurationMs",
			headerName: "Duration",
			width: 100,
			valueFormatter: (value) => formatDuration(value as number | null),
		},
	];

	const productCell = (name: string, shopName: string, shopType?: number | null, imageUrl?: string | null) => (
		<Box sx={{ display: "flex", alignItems: "center", gap: 1.25, minWidth: 0, height: "100%" }}>
			<Avatar
				variant="rounded"
				src={imageUrl ?? undefined}
				alt=""
				sx={{
					width: 48,
					height: 48,
					flexShrink: 0,
					borderRadius: "10px",
					bgcolor: tokens.imageGround,
					border: `1px solid ${tokens.line}`,
					"& img": { objectFit: "contain" },
				}}
			>
				<StoreLogo shopType={shopType} size={22} decorative />
			</Avatar>
			<Box sx={{ minWidth: 0 }}>
				<Typography title={name} sx={{ fontSize: 13.5, fontWeight: 600, lineHeight: 1.3, whiteSpace: "normal", display: "-webkit-box", WebkitLineClamp: 2, WebkitBoxOrient: "vertical", overflow: "hidden" }}>{name}</Typography>
				<Box sx={{ display: "flex", alignItems: "center", gap: 0.5, mt: 0.25 }}>
					<StoreLogo shopType={shopType} size={14} decorative />
					<Typography sx={{ fontSize: 12, color: tokens.ink2 }}>{shopName}</Typography>
				</Box>
			</Box>
		</Box>
	);

	const reviewColumns: GridColDef<ProductMatchReviewItem>[] = [
		{
			field: "sourceName",
			headerName: "Source",
			minWidth: 200,
			flex: 1,
			renderCell: (params) =>
				productCell(params.row.sourceName, params.row.sourceShopName, params.row.sourceShopType, params.row.sourceImageUrl),
		},
		{
			field: "targetName",
			headerName: "Target",
			minWidth: 200,
			flex: 1,
			renderCell: (params) =>
				productCell(params.row.targetName, params.row.targetShopName, params.row.targetShopType, params.row.targetImageUrl),
		},
		{
			field: "score",
			headerName: "Score",
			width: 90,
			valueFormatter: (value) => (Number.isFinite(Number(value)) ? Number(value).toFixed(4) : "-"),
		},
		{ field: "method", headerName: "Method", width: 100 },
		{
			field: "updatedAt",
			headerName: "Updated",
			width: 150,
			valueFormatter: (value) => shortDateTime(value as string | null),
		},
		{
			field: "actions",
			headerName: "Update",
			minWidth: 350,
			flex: 1,
			sortable: false,
			filterable: false,
			renderCell: (params) => {
				const row = params.row;
				const edit = getReviewEdit(row);
				const isSaving = reviewSavingIds.includes(row.matchId);
				const isSame = edit.matchType === "same_product";
				return (
					<Box sx={{ display: "flex", alignItems: "center", gap: 1, height: "100%" }}>
						<TextField
							select
							size="small"
							label="Match type"
							value={edit.matchType}
							onChange={(e) =>
								setReviewEdit(row, { matchType: e.target.value as "same_product" | "comparable" })
							}
							sx={{ width: 140 }}
						>
							<MenuItem value="same_product">Same product</MenuItem>
							<MenuItem value="comparable">Comparable</MenuItem>
						</TextField>
						<TextField
							label="Score"
							size="small"
							type="number"
							value={edit.score}
							disabled={isSame}
							onChange={(e) => setReviewEdit(row, { score: e.target.value })}
							inputProps={{ min: 0.5, max: 0.9999, step: 0.0001 }}
							sx={{ width: 92 }}
						/>
						<Button variant="contained" size="small" disabled={isSaving} onClick={() => saveReview(row)} sx={{ height: 40 }}>
							{isSaving ? "Saving…" : "Save"}
						</Button>
					</Box>
				);
			},
		},
	];

	const desktopSchedules = (
		<>
			<Section
				title="Health"
				action={
					<SegmentedControl
						ariaLabel="Time range"
						value={rangeHours}
						height={34}
						options={[
							{ value: 24, label: "Last 24h" },
							{ value: 168, label: "Last 7d" },
							{ value: 720, label: "Last 30d" },
						]}
						onChange={(v) => setRangeHours(v)}
					/>
				}
			>
				<Box sx={{ display: "flex", flexWrap: "wrap", gap: "12px 40px", opacity: loadingHealth ? 0.6 : 1 }}>
					<InlineStat label="Total runs" value={String(health?.totalRuns ?? 0)} />
					<InlineStat label="Failed" value={String(health?.failedRuns ?? 0)} tone={health?.failedRuns ? "bad" : undefined} />
					<InlineStat label="Failure rate" value={health ? `${Math.round(health.failureRate * 100)}%` : "0%"} />
					<InlineStat label="Last failure" value={health?.lastFailureAtUtc ? shortDateTime(health.lastFailureAtUtc) : "—"} />
				</Box>
				{health?.recentFailures?.length ? (
					<Box sx={{ mt: 2, pt: 1.5, borderTop: `1px solid ${tokens.line}` }}>
						<SubHeading>Recent failures</SubHeading>
						{health.recentFailures.map((failure) => (
							<Typography
								key={`${failure.source}:${failure.jobName}:${failure.startTimeUtc}`}
								sx={{ fontSize: 13.5, lineHeight: 1.6, color: tokens.ink2, overflowWrap: "anywhere" }}
							>
								<Box component="strong" sx={{ color: tokens.ink, fontWeight: 600 }}>
									{failure.jobName}
								</Box>{" "}
								({failure.source}) · {shortDateTime(failure.startTimeUtc)}
								{failure.errorMessage ? ` — ${failure.errorMessage}` : ""}
							</Typography>
						))}
					</Box>
				) : null}
			</Section>

			<Section title="Schedules" action={<Hint>Select a row to see its runs</Hint>}>
				<DataGrid
					rows={schedules}
					columns={scheduleColumns}
					getRowId={(row) => scheduleKey(row)}
					autoHeight
					loading={loadingSchedules}
					pageSizeOptions={[10, 25, 50]}
					initialState={{ pagination: { paginationModel: { pageSize: 10, page: 0 } } }}
					rowSelectionModel={scheduleSelection}
					onRowSelectionModelChange={(model) => {
						const nextId = model.ids.size > 0 ? Array.from(model.ids)[0] : null;
						setSelectedKey(nextId ? String(nextId) : null);
					}}
					sx={gridSx}
				/>
			</Section>

			<Box sx={{ display: "grid", gridTemplateColumns: { sm: "minmax(0, 1fr)", md: "repeat(2, minmax(0, 1fr))" }, gap: 2.5, alignItems: "stretch" }}>
				<Section title="Selected job">
					{selectedSchedule ? (
						<Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
							<Box
								sx={{
									display: "grid",
									gridTemplateColumns: "auto minmax(0, 1fr)",
									gap: "8px 16px",
									fontSize: 13.5,
								}}
							>
								<Box component="span" sx={{ color: tokens.ink2 }}>Job</Box>
								<Box component="span" sx={{ fontWeight: 600, overflowWrap: "anywhere" }}>
									{selectedSchedule.jobName} ({selectedSchedule.source})
								</Box>
								<Box component="span" sx={{ color: tokens.ink2 }}>Schedule</Box>
								<Box component="span" sx={{ fontFamily: MONO, fontSize: 13 }}>{selectedSchedule.scheduleExpression || "-"}</Box>
								<Box component="span" sx={{ color: tokens.ink2 }}>Time zone</Box>
								<Box component="span">{selectedSchedule.timezone}</Box>
								<Box component="span" sx={{ color: tokens.ink2 }}>Next run</Box>
								<Box component="span">{shortDateTime(selectedSchedule.nextFireTimeUtc)}</Box>
								<Box component="span" sx={{ color: tokens.ink2 }}>Last run</Box>
								<Box component="span" sx={{ display: "flex", alignItems: "center", gap: 1, flexWrap: "wrap" }}>
									{shortDateTime(selectedSchedule.lastRunTimeUtc)}
									{selectedSchedule.lastRunStatus && <StatusPill status={selectedSchedule.lastRunStatus} />}
								</Box>
								<Box component="span" sx={{ color: tokens.ink2 }}>Last error</Box>
								<Box component="span" sx={{ fontFamily: selectedSchedule.lastRunErrorMessage ? MONO : undefined, fontSize: selectedSchedule.lastRunErrorMessage ? 12.5 : undefined, color: selectedSchedule.lastRunErrorMessage ? "#8F1D14" : tokens.ink2, overflowWrap: "anywhere" }}>
									{selectedSchedule.lastRunErrorMessage || "—"}
								</Box>
							</Box>
							{runStrip.length > 0 && (
								<Box>
									<SubHeading>Last {runStrip.length} runs</SubHeading>
									<Box
										role="img"
										aria-label={`Last ${runStrip.length} runs: ${runStrip.filter((r) => r.status === "fail").length} failed`}
										sx={{ display: "flex", gap: "3px", flexWrap: "wrap" }}
									>
										{runStrip.map((r) => (
											<Box
												key={r.id}
												title={`${shortDateTime(r.startTime)} · ${r.status}`}
												sx={{ width: 10, height: 20, borderRadius: "3px", bgcolor: r.status === "fail" ? BAD : r.status === "success" ? "#7FC79E" : tokens.lineStrong }}
											/>
										))}
									</Box>
								</Box>
							)}
						</Box>
					) : (
						<Hint>Select a schedule to view details.</Hint>
					)}
				</Section>

				<Section
					title="Failure-rate trend"
					action={
						<SegmentedControl
							ariaLabel="Trend bucket"
							value={trendBucket}
							height={34}
							options={[
								{ value: "day", label: "Daily" },
								{ value: "hour", label: "Hourly" },
							]}
							onChange={(v) => setTrendBucket(v)}
						/>
					}
				>
					<ResponsiveContainer width="100%" height={240}>
						<LineChart data={trendData} margin={{ left: -16, right: 8, top: 8 }}>
							<CartesianGrid strokeDasharray="3 3" stroke={tokens.line} />
							<XAxis dataKey="label" tick={{ fontSize: 11, fill: tokens.ink2 }} />
							<YAxis tickFormatter={(value) => `${Math.round(value * 100)}%`} domain={[0, 1]} tick={{ fontSize: 11, fill: tokens.ink2 }} />
							<Tooltip formatter={(value) => `${Math.round(Number(value) * 100)}%`} />
							<Line type="monotone" dataKey="failureRate" stroke={BAD} strokeWidth={2} dot={false} />
						</LineChart>
					</ResponsiveContainer>
				</Section>
			</Box>

			<Section
				title="Recent runs"
				action={
					stats ? (
						<Box sx={{ display: "flex", flexWrap: "wrap", gap: "4px 20px", fontSize: 13, color: tokens.ink2 }}>
							<span>
								Total <Box component="strong" sx={{ color: tokens.ink }}>{stats.totalRuns}</Box>
							</span>
							<span>
								Failed <Box component="strong" sx={{ color: stats.failedRuns ? BAD : tokens.ink }}>{stats.failedRuns}</Box>
							</span>
							<span>
								Failure rate <Box component="strong" sx={{ color: tokens.ink }}>{Math.round(stats.failureRate * 100)}%</Box>
							</span>
							<span>
								Avg duration <Box component="strong" sx={{ color: tokens.ink }}>{formatDuration(stats.averageDurationMs)}</Box>
							</span>
						</Box>
					) : undefined
				}
			>
				<DataGrid
					rows={runs}
					columns={runColumns}
					getRowId={(row) => row.id}
					autoHeight
					loading={loadingRuns || loadingStats}
					pageSizeOptions={[10, 25, 50]}
					initialState={{ pagination: { paginationModel: { pageSize: 10, page: 0 } } }}
					disableRowSelectionOnClick
					sx={gridSx}
				/>
			</Section>
		</>
	);

	const desktopPrematch = (
		<>
			<Section
				title="Batch pre-match"
				action={
					<Button variant="contained" onClick={runPreMatch} disabled={preMatchLoading} sx={{ height: 44, px: 3 }}>
						{preMatchLoading ? "Starting..." : "Run pre-match"}
					</Button>
				}
			>
				<Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 2 }}>
					<Box sx={{ display: "flex", alignItems: "center", gap: 1, width: 320 }}>
						{shopBox(preMatchRequest.sourceShop)}
						<IconButton
							aria-label="Swap source and target shop"
							onClick={() => setPreMatchRequest((prev) => ({ ...prev, sourceShop: prev.targetShop, targetShop: prev.sourceShop }))}
							sx={{ width: 44, height: 44, flexShrink: 0, border: `1px solid ${tokens.line}`, borderRadius: "12px", color: tokens.brand }}
						>
							<SwapHorizIcon />
						</IconButton>
						{shopBox(preMatchRequest.targetShop)}
					</Box>
					<TextField
						select
						label="Mode"
						size="small"
						value={preMatchRequest.mode ?? "incremental"}
						onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, mode: String(e.target.value) }))}
						sx={{ width: 150, ...fieldSx }}
					>
						<MenuItem value="incremental">Incremental</MenuItem>
						<MenuItem value="full">Full</MenuItem>
					</TextField>
					<TextField
						label="Limit"
						size="small"
						type="number"
						value={preMatchRequest.limit ?? 0}
						onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, limit: Number(e.target.value) }))}
						inputProps={{ min: 0 }}
						sx={{ width: 110, ...fieldSx }}
					/>
					<TextField
						label="Top N"
						size="small"
						type="number"
						value={preMatchRequest.topN ?? 20}
						onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, topN: Number(e.target.value) }))}
						inputProps={{ min: 1, max: 50 }}
						sx={{ width: 90, ...fieldSx }}
					/>
					<TextField
						label="Resume job ID"
						size="small"
						placeholder="Optional"
						value={preMatchRequest.resumeJobId ?? ""}
						onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, resumeJobId: e.target.value }))}
						InputLabelProps={{ shrink: true }}
						sx={{ width: 220, ...fieldSx }}
					/>
					<FormControlLabel
						control={
							<Switch
								checked={Boolean(preMatchRequest.useLlm)}
								onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, useLlm: e.target.checked }))}
							/>
						}
						label="Use LLM"
						sx={{ m: 0, "& .MuiFormControlLabel-label": { fontSize: 14, fontWeight: 600 } }}
					/>
					<FormControlLabel
						control={
							<Switch
								checked={Boolean(preMatchRequest.force)}
								onChange={(e) => setPreMatchRequest((prev) => ({ ...prev, force: e.target.checked }))}
							/>
						}
						label="Force re-match"
						sx={{ m: 0, "& .MuiFormControlLabel-label": { fontSize: 14, fontWeight: 600 } }}
					/>
				</Box>

				{preMatchError && (
					<Alert severity="error" sx={{ mt: 2 }}>
						{preMatchError}
					</Alert>
				)}

				{preMatchStatus && (
					<Box
						sx={{
							mt: 2,
							pt: 1.75,
							borderTop: `1px solid ${tokens.line}`,
							display: "flex",
							flexWrap: "wrap",
							alignItems: "center",
							gap: "8px 20px",
							fontSize: 13.5,
							color: tokens.ink2,
						}}
					>
						<StatusPill status={preMatchStatus.status} />
						<Box component="strong" sx={{ color: tokens.ink }}>
							{shopLabel(preMatchStatus.sourceShop)} → {shopLabel(preMatchStatus.targetShop)}
						</Box>
						<Box sx={{ display: "flex", alignItems: "center", gap: 1, fontVariantNumeric: "tabular-nums" }}>
							<LinearProgress
								variant="determinate"
								value={preMatchProgress}
								aria-label="Pre-match progress"
								sx={{ width: 160, height: 6, borderRadius: 6, bgcolor: tokens.line }}
							/>
							{preMatchStatus.processed}/{preMatchStatus.total}
						</Box>
						<span>
							Matched <Box component="strong" sx={{ color: tokens.save }}>{preMatchStatus.matched}</Box> · Comparable{" "}
							<Box component="strong" sx={{ color: tokens.ink }}>{preMatchStatus.comparable}</Box> · Failed{" "}
							<Box component="strong" sx={{ color: BAD }}>{preMatchStatus.failed}</Box>
						</span>
						<span>
							{preMatchStatus.mode} · Top {preMatchStatus.topN} · Limit {preMatchStatus.limitNum} · LLM{" "}
							{preMatchStatus.useLlm ? "on" : "off"}
						</span>
						<span>Updated {shortDateTime(preMatchStatus.updatedAt)}</span>
						{preMatchJobId && <Box component="span" sx={{ fontFamily: MONO, fontSize: 12 }}>Job {preMatchJobId}</Box>}
						<Button
							variant="outlined"
							onClick={() => void refreshPreMatchStatus()}
							disabled={statusRefreshing}
							startIcon={<RefreshIcon />}
							sx={{ height: 36 }}
						>
							Refresh status
						</Button>
						{preMatchStatus.errorMessage && (
							<Box sx={{ flexBasis: "100%", color: BAD }}>Error: {preMatchStatus.errorMessage}</Box>
						)}
					</Box>
				)}
			</Section>

			<Section title="Match jobs">
				<Box sx={{ mb: 2 }}>{matchJobFilters}</Box>
				{matchJobsError && (
					<Alert severity="error" sx={{ mb: 2 }}>
						{matchJobsError}
					</Alert>
				)}
				<DataGrid
					rows={matchJobs}
					columns={matchJobColumns}
					getRowId={(row) => row.id}
					autoHeight
					loading={matchJobsLoading}
					rowCount={matchJobsTotal}
					paginationMode="server"
					paginationModel={matchJobsPagination}
					onPaginationModelChange={(model) => setMatchJobsPagination(model)}
					pageSizeOptions={[10, 20, 50]}
					disableRowSelectionOnClick
					sx={gridSx}
				/>
			</Section>

			<Section title="Match review" action={<Hint>Score range {reviewScoreMin.toFixed(2)}–{reviewScoreMax.toFixed(2)}</Hint>}>
				{reviewError && (
					<Alert severity="error" sx={{ mb: 2 }}>
						{reviewError}
					</Alert>
				)}
				<DataGrid
					rows={reviewItems}
					columns={reviewColumns}
					getRowId={(row) => row.matchId}
					autoHeight
					rowHeight={84}
					loading={reviewLoading}
					rowCount={reviewTotal}
					paginationMode="server"
					paginationModel={reviewPagination}
					onPaginationModelChange={(model) => setReviewPagination(model)}
					pageSizeOptions={[10, 20, 50]}
					disableRowSelectionOnClick
					sx={{ ...gridSx, "& .MuiDataGrid-cell": { display: "flex", alignItems: "center" } }}
				/>
			</Section>
		</>
	);

	return (
		<Box sx={{ display: "flex", flexDirection: "column", gap: { xs: 1.5, sm: 2.5 } }}>
			<Box
				sx={{
					display: "flex",
					flexWrap: "wrap",
					alignItems: "center",
					justifyContent: "space-between",
					gap: { xs: 1.5, sm: 2 },
				}}
			>
				<Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: { xs: 1.5, sm: 3 }, flex: { xs: "1 1 100%", sm: "0 1 auto" } }}>
					<Typography
						component="h1"
						sx={{
							fontFamily: DISPLAY_FONT,
							fontWeight: 800,
							fontSize: { xs: 24, sm: 34 },
							letterSpacing: { xs: "-0.02em", sm: "-0.03em" },
							lineHeight: 1.12,
							flex: { xs: 1, sm: "none" },
						}}
					>
						Admin
					</Typography>
					{isXs && adminTab === "schedules" && (
						<IconButton
							aria-label="Refresh schedules and health"
							onClick={refreshAll}
							sx={{ width: 44, height: 44, border: `1px solid ${tokens.line}`, borderRadius: "12px", bgcolor: tokens.surface }}
						>
							<RefreshIcon />
						</IconButton>
					)}
					<SegmentedControl
						ariaLabel="Admin sections"
						value={adminTab}
						fullWidth={isXs}
						sx={{ flex: { xs: "1 1 100%", sm: "none" } }}
						options={[
							{ value: "schedules", label: "Schedules" },
							{ value: "prematch", label: "Pre-match" },
						]}
						onChange={(value) => navigate(value === "prematch" ? "/admin/prematch" : "/admin/schedules")}
					/>
				</Box>
				{!isXs && adminTab === "schedules" && (
					<Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 1 }}>
						{cleanResult !== null && (
							<Typography role="status" sx={{ fontSize: 13.5, color: tokens.save, fontWeight: 600 }}>
								Deleted {cleanResult} rows
							</Typography>
						)}
						{cleanError && (
							<Typography role="alert" sx={{ fontSize: 13.5, color: BAD }}>
								{cleanError}
							</Typography>
						)}
						<Button variant="outlined" startIcon={<RefreshIcon />} onClick={refreshAll} sx={{ height: 44 }}>
							Refresh
						</Button>
						<Button
							variant="outlined"
							disabled={cleanLoading}
							onClick={() => setCleanConfirmOpen(true)}
							sx={{ height: 44, color: tokens.promo, borderColor: "#E2C48A", "&:hover": { borderColor: tokens.promo, bgcolor: tokens.promoTint } }}
						>
							{cleanLoading ? "Cleaning…" : "Clean price history"}
						</Button>
					</Box>
				)}
			</Box>

			{error && <Alert severity="error">{error}</Alert>}

			<ConfirmSheet
				open={cleanConfirmOpen}
				title="Clean price history now?"
				confirmLabel="Clean now"
				onClose={() => setCleanConfirmOpen(false)}
				onConfirm={runCleanNow}
			>
				<Typography sx={{ fontSize: 14, lineHeight: 1.5, color: tokens.ink2 }}>
					Old price history rows are deleted permanently. Price trends older than the retention window
					will be gone.
				</Typography>
			</ConfirmSheet>

			{!isXs && adminTab === "schedules" && desktopSchedules}
			{!isXs && adminTab === "prematch" && desktopPrematch}

			{isXs && adminTab === "schedules" && (
				<>
					<Section
						title="Health"
						action={
							<SegmentedControl
								ariaLabel="Time range"
								value={rangeHours}
								height={34}
								options={[
									{ value: 24, label: isXs ? "24h" : "Last 24h" },
									{ value: 168, label: isXs ? "7d" : "Last 7d" },
									{ value: 720, label: isXs ? "30d" : "Last 30d" },
								]}
								onChange={(v) => setRangeHours(v)}
							/>
						}
					>
						<Box
							sx={{
								display: "grid",
								gridTemplateColumns: { xs: "repeat(2, minmax(0, 1fr))", md: "repeat(4, minmax(0, 1fr))" },
								gap: { xs: 1, sm: 1.25 },
								opacity: loadingHealth ? 0.6 : 1,
							}}
						>
							<StatTile label="Total runs" value={String(health?.totalRuns ?? 0)} />
							<StatTile label="Failed" value={String(health?.failedRuns ?? 0)} tone={health?.failedRuns ? "bad" : undefined} />
							<StatTile label="Failure rate" value={health ? `${Math.round(health.failureRate * 100)}%` : "0%"} />
							<StatTile label="Last failure" value={health?.lastFailureAtUtc ? shortDateTime(health.lastFailureAtUtc) : "—"} small />
						</Box>
						{health?.recentFailures?.length ? (
							<Box sx={{ mt: 1.75, borderTop: `1px solid ${tokens.line}`, pt: 1.25 }}>
								<SubHeading>Recent failures</SubHeading>
								<Box
									component="ul"
									sx={{
										listStyle: "none",
										m: 0,
										p: 0,
										display: "grid",
										gridTemplateColumns: { xs: "minmax(0, 1fr)", md: "repeat(2, minmax(0, 1fr))" },
										columnGap: 3,
									}}
								>
									{health.recentFailures.map((failure) => (
										<Box
											component="li"
											key={`${failure.source}:${failure.jobName}:${failure.startTimeUtc}`}
											sx={{ display: "flex", gap: 1.25, py: 1, borderTop: `1px solid ${tokens.line}`, "&:first-of-type": { borderTop: "none" } }}
										>
											<Box sx={{ width: 8, height: 8, borderRadius: "50%", bgcolor: BAD, mt: "6px", flexShrink: 0 }} />
											<Box sx={{ minWidth: 0 }}>
												<Typography sx={{ fontSize: 13.5, fontWeight: 600, overflowWrap: "anywhere" }}>{failure.jobName}</Typography>
												<Typography sx={{ fontSize: 12.5, color: tokens.ink2 }}>
													{failure.source} · {shortDateTime(failure.startTimeUtc)}
												</Typography>
												{failure.errorMessage && (
													<Typography
														title={failure.errorMessage}
														sx={{
															fontSize: 12.5,
															color: tokens.ink2,
															lineHeight: 1.4,
															display: "-webkit-box",
															WebkitLineClamp: 2,
															WebkitBoxOrient: "vertical",
															overflow: "hidden",
															overflowWrap: "anywhere",
														}}
													>
														{failure.errorMessage}
													</Typography>
												)}
											</Box>
										</Box>
									))}
								</Box>
							</Box>
						) : null}
					</Section>

					<Box
						sx={{
							display: "grid",
							gridTemplateColumns: { xs: "minmax(0, 1fr)", md: "minmax(0, 1.6fr) minmax(0, 1fr)" },
							gap: { xs: 1.5, sm: 2.5 },
							alignItems: "start",
						}}
					>
						<Section title={`Jobs · ${schedules.length}`} flush action={!isXs ? <Hint>Select a job to see its runs</Hint> : undefined}>
							{loadingSchedules && schedules.length === 0 ? (
								<Box sx={{ display: "flex", justifyContent: "center", py: 4 }}>
									<CircularProgress size={24} />
								</Box>
							) : (
								<Box sx={{ overflowX: "auto" }}>
									<Box sx={{ minWidth: { xs: 0, md: 620 } }}>
										{!isXs && (
											<Box sx={{ ...jobGridSx, py: 1.25, bgcolor: tokens.imageGround, borderTop: `1px solid ${tokens.line}`, borderBottom: `1px solid ${tokens.line}`, fontSize: 12, fontWeight: 700, letterSpacing: ".04em", textTransform: "uppercase", color: tokens.ink2 }}>
												<span>Job</span>
												<span>Status</span>
												<span>Next run</span>
												<span>Last run</span>
											</Box>
										)}
										{schedules.slice(schedPaging.page * schedPaging.pageSize, (schedPaging.page + 1) * schedPaging.pageSize).map((s, i) => {
											const key = scheduleKey(s);
											const selected = key === selectedKey;
											return (
												<ButtonBase
													key={key}
													onClick={() => setSelectedKey(key)}
													aria-pressed={selected}
													sx={{
														...(isXs
															? { display: "flex", alignItems: "center", gap: 1.25, px: 1.75, py: 1.25 }
															: { ...jobGridSx, py: 1.5 }),
														width: "100%",
														minHeight: 60,
														textAlign: "left",
														color: tokens.ink,
														borderTop: isXs && i === 0 ? `1px solid ${tokens.line}` : "none",
														borderBottom: `1px solid ${tokens.line}`,
														bgcolor: selected ? tokens.brandTint : tokens.surface,
														boxShadow: selected ? `inset 3px 0 0 ${tokens.brand}` : "none",
														"&:hover": { bgcolor: selected ? tokens.brandTint : tokens.imageGround },
														"&.Mui-focusVisible": { outline: `2px solid ${tokens.brand}`, outlineOffset: -2 },
													}}
												>
													<Box sx={{ flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: 0.25 }}>
														<Typography sx={{ fontSize: { xs: 13.5, sm: 14 }, fontWeight: 600, overflowWrap: "anywhere" }}>
															{s.jobName}
														</Typography>
														<Typography sx={{ fontSize: 12.5, color: tokens.ink2 }}>
															{s.source}
															{isXs && ` · Next ${shortDateTime(s.nextFireTimeUtc)}`}
														</Typography>
													</Box>
													<Box>
														<StatusPill status={jobStatus(s)} />
													</Box>
													{!isXs && <Typography sx={{ fontSize: 13.5 }}>{shortDateTime(s.nextFireTimeUtc)}</Typography>}
													{!isXs && <Typography sx={{ fontSize: 13.5 }}>{shortDateTime(s.lastRunTimeUtc)}</Typography>}
												</ButtonBase>
											);
										})}
									</Box>
								</Box>
							)}
							{isXs && schedules.length > 0 && (
									<PhonePager
										page={schedPaging.page}
										pageSize={schedPaging.pageSize}
										total={schedules.length}
										pageSizes={[10, 25, 50]}
										onChange={setSchedPaging}
									/>
								)}
							</Section>

							<Section title="Selected job">
							{selectedSchedule ? (
								<Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
									<Box sx={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 1 }}>
										<Typography sx={{ fontSize: 15, fontWeight: 700, overflowWrap: "anywhere" }}>
											{selectedSchedule.jobName}
										</Typography>
										<StatusPill status={jobStatus(selectedSchedule)} />
									</Box>
									<Box
										sx={{
											display: "grid",
											gridTemplateColumns: "auto minmax(0, 1fr)",
											gap: "8px 14px",
											p: 1.5,
											bgcolor: tokens.ground,
											borderRadius: "12px",
											fontSize: 13,
										}}
									>
										<Box component="span" sx={{ color: tokens.ink2 }}>Source</Box>
										<Box component="span" sx={{ fontWeight: 600 }}>{selectedSchedule.source}</Box>
										<Box component="span" sx={{ color: tokens.ink2 }}>Schedule</Box>
										<Box component="span" sx={{ fontWeight: 600, fontFamily: MONO, fontSize: 12.5, overflowWrap: "anywhere" }}>
											{selectedSchedule.scheduleExpression || "-"}
										</Box>
										<Box component="span" sx={{ color: tokens.ink2 }}>Time zone</Box>
										<Box component="span" sx={{ fontWeight: 600 }}>{selectedSchedule.timezone}</Box>
										<Box component="span" sx={{ color: tokens.ink2 }}>Next run</Box>
										<Box component="span" sx={{ fontWeight: 600 }}>{shortDateTime(selectedSchedule.nextFireTimeUtc)}</Box>
										<Box component="span" sx={{ color: tokens.ink2 }}>Last run</Box>
										<Box component="span" sx={{ fontWeight: 600 }}>
											{shortDateTime(selectedSchedule.lastRunTimeUtc)}
											{selectedSchedule.lastRunDurationMs != null && ` · ${formatDuration(selectedSchedule.lastRunDurationMs)}`}
										</Box>
									</Box>
									{selectedSchedule.lastRunErrorMessage && (
										<Box
											sx={{
												p: 1.25,
												borderRadius: "12px",
												bgcolor: BAD_TINT,
												color: "#8F1D14",
												fontFamily: MONO,
												fontSize: 12,
												lineHeight: 1.5,
												overflowWrap: "anywhere",
											}}
										>
											{selectedSchedule.lastRunErrorMessage}
										</Box>
									)}
									{stats && (
										<Box sx={{ display: "grid", gridTemplateColumns: "repeat(2, minmax(0, 1fr))", gap: 1 }}>
											<StatTile label="Runs" value={String(stats.totalRuns)} compact />
											<StatTile label="Failed" value={String(stats.failedRuns)} tone={stats.failedRuns ? "bad" : undefined} compact />
											<StatTile label="Failure rate" value={`${Math.round(stats.failureRate * 100)}%`} compact />
											<StatTile label="Avg duration" value={formatDuration(stats.averageDurationMs)} compact />
										</Box>
									)}
									{runStrip.length > 0 && (
										<Box>
											<SubHeading>Last {runStrip.length} runs</SubHeading>
											<Box
												role="img"
												aria-label={`Last ${runStrip.length} runs: ${runStrip.filter((r) => r.status === "fail").length} failed`}
												sx={{ display: "flex", gap: "3px", flexWrap: "wrap" }}
											>
												{runStrip.map((r) => (
													<Box
														key={r.id}
														title={`${shortDateTime(r.startTime)} · ${r.status}`}
														sx={{
															width: 10,
															height: 20,
															borderRadius: "3px",
															bgcolor: r.status === "fail" ? BAD : r.status === "success" ? "#7FC79E" : tokens.lineStrong,
														}}
													/>
												))}
											</Box>
										</Box>
									)}
								</Box>
							) : (
								<Hint>Select a job to see its details.</Hint>
							)}
						</Section>
					</Box>

					<Section
						title="Failure-rate trend"
						action={
							<SegmentedControl
								ariaLabel="Trend bucket"
								value={trendBucket}
								height={34}
								options={[
									{ value: "day", label: "Daily" },
									{ value: "hour", label: "Hourly" },
								]}
								onChange={(v) => setTrendBucket(v)}
							/>
						}
					>
						<ResponsiveContainer width="100%" height={isXs ? 200 : 260}>
							<LineChart data={trendData} margin={{ left: -16, right: 8, top: 8 }}>
								<CartesianGrid strokeDasharray="3 3" stroke={tokens.line} />
								<XAxis dataKey="label" tick={{ fontSize: 11, fill: tokens.ink2 }} />
								<YAxis
									tickFormatter={(value) => `${Math.round(value * 100)}%`}
									domain={[0, 1]}
									tick={{ fontSize: 11, fill: tokens.ink2 }}
								/>
								<Tooltip formatter={(value) => `${Math.round(Number(value) * 100)}%`} />
								<Line type="monotone" dataKey="failureRate" stroke={BAD} strokeWidth={2} dot={false} />
							</LineChart>
						</ResponsiveContainer>
					</Section>

					<Section title="Recent runs" flush={isXs}>
						{loadingRuns || loadingStats ? (
							<Box sx={{ display: "flex", alignItems: "center", gap: 1, p: isXs ? 2 : 0 }}>
								<CircularProgress size={20} />
								<Typography variant="body2">Loading runs...</Typography>
							</Box>
						) : isXs ? (
							runs.length === 0 ? (
								<Box sx={{ px: 2, pb: 2 }}>
									<Hint>No runs yet.</Hint>
								</Box>
							) : (
								<>
									{runs.slice(runsPaging.page * runsPaging.pageSize, (runsPaging.page + 1) * runsPaging.pageSize).map((r) => (
										<Box key={r.id} sx={{ px: 1.75, py: 1.25, borderTop: `1px solid ${tokens.line}`, display: "flex", flexDirection: "column", gap: 0.5 }}>
											<Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 1 }}>
												<Typography sx={{ fontSize: 13.5, fontWeight: 600 }}>{shortDateTime(r.startTime)}</Typography>
												<StatusPill status={r.status} />
											</Box>
											<Typography sx={{ fontSize: 12.5, color: tokens.ink2 }}>
												{formatDuration(r.durationMs)}
												{r.environment ? ` · ${r.environment}` : ""}
											</Typography>
											{r.errorMessage && (
												<Typography sx={{ fontSize: 12, color: "#8F1D14", fontFamily: MONO, overflowWrap: "anywhere", lineHeight: 1.45 }}>
													{r.errorMessage}
												</Typography>
											)}
										</Box>
									))}
									<PhonePager
										page={runsPaging.page}
										pageSize={runsPaging.pageSize}
										total={runs.length}
										pageSizes={[10, 25, 50]}
										onChange={setRunsPaging}
									/>
								</>
							)
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
								sx={gridSx}
							/>
						)}
					</Section>

					<Section title="Maintenance">
						<Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", justifyContent: "space-between", gap: 1.5 }}>
							<Typography sx={{ fontSize: 13.5, color: tokens.ink2, lineHeight: 1.5, flex: "1 1 260px" }}>
								Delete old price history rows now. CleanPriceHistoryJob also does this on its schedule.
							</Typography>
							<Button
								variant="outlined"
								disabled={cleanLoading}
								onClick={() => setCleanConfirmOpen(true)}
								sx={{
									height: 44,
									flex: { xs: "1 1 100%", sm: "none" },
									color: tokens.promo,
									borderColor: "#E2C48A",
									"&:hover": { borderColor: tokens.promo, bgcolor: tokens.promoTint },
								}}
							>
								{cleanLoading ? "Cleaning…" : "Clean price history"}
							</Button>
						</Box>
						{cleanResult !== null && (
							<Typography role="status" sx={{ mt: 1, fontSize: 13.5, color: tokens.save, fontWeight: 600 }}>
								Deleted {cleanResult} rows.
							</Typography>
						)}
						{cleanError && (
							<Typography role="alert" sx={{ mt: 1, fontSize: 13.5, color: BAD }}>
								{cleanError}
							</Typography>
						)}
					</Section>

				</>
			)}

			{isXs && adminTab === "prematch" && (
				<>
					<Box
						sx={{
							display: "grid",
							gridTemplateColumns: { xs: "minmax(0, 1fr)", md: "340px minmax(0, 1fr)" },
							gap: { xs: 1.5, sm: 2.5 },
							alignItems: "start",
						}}
					>
						<Section title="Batch pre-match">
							<Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
								<Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
									{shopBox(preMatchRequest.sourceShop)}
									<IconButton
										aria-label="Swap source and target shop"
										onClick={() =>
											setPreMatchRequest((prev) => ({ ...prev, sourceShop: prev.targetShop, targetShop: prev.sourceShop }))
										}
										sx={{ width: 44, height: 44, flexShrink: 0, border: `1px solid ${tokens.line}`, borderRadius: "12px", color: tokens.brand }}
									>
										<SwapHorizIcon />
									</IconButton>
									{shopBox(preMatchRequest.targetShop)}
								</Box>

								{isXs ? (
									<>
										<ButtonBase
											onClick={() => setOptionsOpen((v) => !v)}
											aria-expanded={optionsOpen}
											sx={{ minHeight: 44, display: "flex", justifyContent: "space-between", textAlign: "left", borderRadius: "10px", px: 0.25 }}
										>
											<Box>
												<Typography sx={{ fontSize: 13.5, fontWeight: 600 }}>Options</Typography>
												<Typography sx={{ fontSize: 12, color: tokens.ink2 }}>
													{preMatchRequest.mode === "full" ? "Full" : "Incremental"} · Limit {preMatchRequest.limit ?? 0} · Top{" "}
													{preMatchRequest.topN ?? 20} · LLM {preMatchRequest.useLlm ? "on" : "off"}
													{preMatchRequest.force ? " · Force" : ""}
												</Typography>
											</Box>
											<ExpandMoreIcon
												aria-hidden="true"
												sx={{ color: tokens.ink2, transition: "transform .15s", transform: optionsOpen ? "rotate(180deg)" : "none" }}
											/>
										</ButtonBase>
										<Collapse in={optionsOpen}>{preMatchOptions}</Collapse>
									</>
								) : (
									preMatchOptions
								)}

								<Button variant="contained" onClick={runPreMatch} disabled={preMatchLoading} sx={{ height: 48, fontSize: 15, fontWeight: 700 }}>
									{preMatchLoading ? "Starting..." : "Run pre-match"}
								</Button>

								{preMatchError && <Alert severity="error">{preMatchError}</Alert>}

								{preMatchStatus && (
									<Box sx={{ p: 1.5, borderRadius: "12px", bgcolor: tokens.ground, display: "flex", flexDirection: "column", gap: 1 }}>
										<Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 1 }}>
											<Typography sx={{ fontSize: 13, fontWeight: 700 }}>
												{shopLabel(preMatchStatus.sourceShop)} → {shopLabel(preMatchStatus.targetShop)}
											</Typography>
											<StatusPill status={preMatchStatus.status} />
										</Box>
										<LinearProgress
											variant="determinate"
											value={preMatchProgress}
											aria-label="Pre-match progress"
											sx={{ height: 6, borderRadius: 6, bgcolor: tokens.line }}
										/>
										<Box sx={{ display: "flex", flexWrap: "wrap", justifyContent: "space-between", gap: 1, fontSize: 12.5, color: tokens.ink2, fontVariantNumeric: "tabular-nums" }}>
											<span>
												{preMatchStatus.processed.toLocaleString()} / {preMatchStatus.total.toLocaleString()}
											</span>
											<span>
												<Box component="strong" sx={{ color: tokens.save }}>{preMatchStatus.matched}</Box> same ·{" "}
												<Box component="strong" sx={{ color: tokens.ink }}>{preMatchStatus.comparable}</Box> comparable ·{" "}
												<Box component="strong" sx={{ color: BAD }}>{preMatchStatus.failed}</Box> failed
											</span>
										</Box>
										<Typography sx={{ fontSize: 12, color: tokens.ink2 }}>
											{preMatchStatus.mode} · Top {preMatchStatus.topN} · Limit {preMatchStatus.limitNum} · LLM{" "}
											{preMatchStatus.useLlm ? "on" : "off"} · updated {shortDateTime(preMatchStatus.updatedAt)}
										</Typography>
										{preMatchJobId && (
											<Typography sx={{ fontSize: 11.5, color: tokens.ink2, fontFamily: MONO, overflowWrap: "anywhere" }}>
												Job {preMatchJobId}
											</Typography>
										)}
										{preMatchStatus.errorMessage && (
											<Typography sx={{ fontSize: 12.5, color: BAD }}>Error: {preMatchStatus.errorMessage}</Typography>
										)}
										<Button
											variant="outlined"
											onClick={() => void refreshPreMatchStatus()}
											disabled={statusRefreshing}
											startIcon={<RefreshIcon />}
											sx={{ height: 40, alignSelf: "flex-start" }}
										>
											Refresh status
										</Button>
									</Box>
								)}
							</Box>
						</Section>

						<Box sx={{ minWidth: 0, display: "flex", flexDirection: "column", gap: 1.5 }}>
							<Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "baseline", justifyContent: "space-between", gap: 1, mx: 0.5 }}>
								<Typography component="h2" sx={{ fontSize: 16, fontWeight: 700 }}>
									To review{" "}
									<Box component="span" sx={{ color: tokens.ink2, fontWeight: 600 }}>
										· {reviewTotal}
									</Box>
								</Typography>
								<Hint>
									Score {reviewScoreMin.toFixed(2)}–{reviewScoreMax.toFixed(2)}
								</Hint>
							</Box>
							{reviewError && <Alert severity="error">{reviewError}</Alert>}
							{reviewLoading && reviewItems.length === 0 ? (
								<Box sx={{ display: "flex", justifyContent: "center", py: 4 }}>
									<CircularProgress size={24} />
								</Box>
							) : reviewItems.length === 0 ? (
								<Box sx={{ p: 3, textAlign: "center", bgcolor: tokens.surface, border: `1px dashed ${tokens.lineStrong}`, borderRadius: "16px", color: tokens.ink2, fontSize: 14 }}>
									Nothing to review in this score range.
								</Box>
							) : (
								reviewItems.map((row) => {
									const edit = getReviewEdit(row);
									const isSaving = reviewSavingIds.includes(row.matchId);
									const isSame = edit.matchType === "same_product";
									return (
										<Box
											component="article"
											key={row.matchId}
											sx={{
												bgcolor: tokens.surface,
												border: `1px solid ${tokens.line}`,
												borderRadius: "16px",
												p: { xs: 1.5, sm: 1.75 },
												display: "flex",
												flexDirection: "column",
												gap: 1.5,
												boxShadow: "0 1px 2px rgba(20,22,30,.04)",
												opacity: reviewLoading ? 0.7 : 1,
											}}
										>
											<Box
												sx={{
													display: "grid",
													gridTemplateColumns: { xs: "minmax(0, 1fr)", sm: "minmax(0, 1fr) 28px minmax(0, 1fr)" },
													alignItems: "center",
													gap: { xs: 0, sm: 1 },
													border: { xs: `1px solid ${tokens.line}`, sm: "none" },
													borderRadius: "12px",
												}}
											>
												<MatchSide name={row.sourceName} shopName={row.sourceShopName} shopType={row.sourceShopType} imageUrl={row.sourceImageUrl} />
												<SwapHorizIcon aria-hidden="true" sx={{ display: { xs: "none", sm: "block" }, color: tokens.ink2, justifySelf: "center" }} />
												<Box sx={{ borderTop: { xs: `1px dashed ${tokens.line}`, sm: "none" } }}>
													<MatchSide name={row.targetName} shopName={row.targetShopName} shopType={row.targetShopType} imageUrl={row.targetImageUrl} />
												</Box>
											</Box>
											<Box sx={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: { xs: 1, sm: "10px 14px" } }}>
												<Box sx={{ display: "flex", alignItems: "center", gap: 1, fontSize: 12.5, color: tokens.ink2, fontWeight: 600, flex: { xs: "1 1 100%", sm: "1 1 auto" } }}>
													Score
													<Box sx={{ width: 72, height: 6, borderRadius: 6, bgcolor: tokens.line, overflow: "hidden" }}>
														<Box
															sx={{
																height: "100%",
																width: `${Math.round(Math.max(0, Math.min(1, row.score)) * 100)}%`,
																bgcolor: row.score >= 0.8 ? tokens.save : row.score >= 0.65 ? tokens.brand : tokens.promo,
															}}
														/>
													</Box>
													<Box component="strong" sx={{ color: tokens.ink, fontVariantNumeric: "tabular-nums" }}>
														{row.score.toFixed(2)}
													</Box>
													<Box component="span" sx={{ fontWeight: 500 }}>
														{row.method ? `· ${row.method}` : ""} · {shortDateTime(row.updatedAt)}
													</Box>
												</Box>
												<SegmentedControl
													ariaLabel="Match type"
													value={edit.matchType}
													fullWidth={isXs}
													sx={{ flex: { xs: "1 1 100%", sm: "none" } }}
													options={[
														{ value: "same_product", label: "Same product" },
														{ value: "comparable", label: "Comparable" },
													]}
													onChange={(v) => setReviewEdit(row, { matchType: v })}
												/>
												{!isSame && (
													<TextField
														label="Score"
														size="small"
														type="number"
														value={edit.score}
														onChange={(e) => setReviewEdit(row, { score: e.target.value })}
														inputProps={{ min: 0.5, max: 0.9999, step: 0.0001 }}
														sx={{ width: { xs: "auto", sm: 120 }, flex: { xs: 1, sm: "none" }, ...fieldSx }}
													/>
												)}
												<Button
													variant="contained"
													disabled={isSaving}
													onClick={() => saveReview(row)}
													sx={{ height: 44, minWidth: 84, bgcolor: tokens.ink, "&:hover": { bgcolor: "#000" } }}
												>
													{isSaving ? "Saving…" : "Save"}
												</Button>
											</Box>
										</Box>
									);
								})
							)}
							{reviewTotal > 0 && (
								<Box sx={{ bgcolor: tokens.surface, border: `1px solid ${tokens.line}`, borderRadius: "16px", overflow: "hidden" }}>
									<PhonePager
										page={reviewPagination.page}
										pageSize={reviewPagination.pageSize}
										total={reviewTotal}
										pageSizes={[10, 20, 50]}
										onChange={setReviewPagination}
										noBorder
									/>
								</Box>
							)}
						</Box>
					</Box>

					<Section
						title={`Match jobs · ${matchJobsTotal}`}
						flush={isXs}
						action={
							isXs ? (
								<Button
									variant="outlined"
									startIcon={<FilterListIcon />}
									onClick={() => setFiltersOpen((v) => !v)}
									aria-expanded={filtersOpen}
									sx={{ height: 40 }}
								>
									Filters
								</Button>
							) : undefined
						}
					>
						{isXs ? (
							<Collapse in={filtersOpen}>
								<Box sx={{ px: 1.75, pb: 1.75 }}>{matchJobFilters}</Box>
							</Collapse>
						) : (
							<Box sx={{ mb: 2 }}>{matchJobFilters}</Box>
						)}

						{matchJobsError && (
							<Alert severity="error" sx={{ mx: isXs ? 1.75 : 0, mb: 1.5 }}>
								{matchJobsError}
							</Alert>
						)}

						{isXs ? (
							<>
								{matchJobsLoading && matchJobs.length === 0 ? (
									<Box sx={{ display: "flex", justifyContent: "center", py: 3 }}>
										<CircularProgress size={22} />
									</Box>
								) : matchJobs.length === 0 ? (
									<Box sx={{ px: 1.75, pb: 2 }}>
										<Hint>No match jobs.</Hint>
									</Box>
								) : (
									matchJobs.map((job) => (
										<Box key={job.id} sx={{ px: 1.75, py: 1.5, borderTop: `1px solid ${tokens.line}`, display: "flex", flexDirection: "column", gap: 0.75 }}>
											<Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 1 }}>
												<Box sx={{ display: "flex", alignItems: "center", gap: 0.75, fontSize: 13.5, fontWeight: 600 }}>
													<StoreLogo shopType={job.sourceShop} size={18} />
													<span aria-hidden="true">→</span>
													<StoreLogo shopType={job.targetShop} size={18} />
													<Box component="span" sx={{ ml: 0.5 }}>
														{job.mode}
														{job.useLlm ? " · LLM" : ""}
													</Box>
												</Box>
												<StatusPill status={job.status} />
											</Box>
											<LinearProgress
												variant="determinate"
												value={job.total > 0 ? Math.min(100, (job.processed / job.total) * 100) : 0}
												aria-label="Progress"
												sx={{ height: 5, borderRadius: 5, bgcolor: tokens.line }}
											/>
											<Typography sx={{ fontSize: 12.5, color: tokens.ink2, fontVariantNumeric: "tabular-nums" }}>
												{job.processed}/{job.total} · {job.matched} same · {job.comparable} comparable · {job.failed} failed
											</Typography>
											<Typography sx={{ fontSize: 11.5, color: tokens.ink2, fontFamily: MONO, overflowWrap: "anywhere" }}>
												{job.id} · {shortDateTime(job.updatedAt)}
											</Typography>
											{job.errorMessage && (
												<Typography sx={{ fontSize: 12, color: BAD, overflowWrap: "anywhere" }}>{job.errorMessage}</Typography>
											)}
										</Box>
									))
								)}
								{matchJobsTotal > 0 && (
									<PhonePager
										page={matchJobsPagination.page}
										pageSize={matchJobsPagination.pageSize}
										total={matchJobsTotal}
										pageSizes={[10, 20, 50]}
										onChange={setMatchJobsPagination}
									/>
								)}
							</>
						) : (
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
								sx={gridSx}
							/>
						)}
					</Section>
				</>
			)}
		</Box>
	);
}

const BAD = "#B42318";
const BAD_TINT = "#FDECEC";
const MONO = 'ui-monospace, "SFMono-Regular", "Cascadia Mono", Menlo, Consolas, monospace';

const fieldSx = { "& .MuiInputBase-root": { minHeight: 44 } } as const;

const switchRowSx = {
	m: 0,
	width: "100%",
	minHeight: 48,
	justifyContent: "space-between",
	borderBottom: `1px solid ${tokens.line}`,
	"& .MuiFormControlLabel-label": { fontSize: 14, fontWeight: 600 },
} as const;

const jobGridSx = {
	display: "grid",
	gridTemplateColumns: "minmax(200px, 2fr) 110px minmax(140px, 1fr) minmax(140px, 1fr)",
	gap: 1.5,
	alignItems: "center",
	px: 2.25,
} as const;

const gridSx = {
	border: `1px solid ${tokens.line}`,
	borderRadius: "12px",
	"& .MuiDataGrid-columnHeaders": { bgcolor: tokens.imageGround },
	"& .MuiDataGrid-columnHeaderTitle": { fontWeight: 700, fontSize: 12.5, color: tokens.ink2 },
} as const;

/** "Thu 20 Nov, 2:00 am" (adds the year when it isn't this year). */
function shortDateTime(value?: string | null): string {
	if (!value) return "—";
	const d = new Date(value);
	if (Number.isNaN(d.getTime())) return "—";
	const sameYear = d.getFullYear() === new Date().getFullYear();
	const day = d.toLocaleDateString("en-AU", {
		weekday: "short",
		day: "numeric",
		month: "short",
		...(sameYear ? {} : { year: "numeric" }),
	});
	return `${day}, ${d.toLocaleTimeString("en-AU", { hour: "numeric", minute: "2-digit" })}`;
}

function Section({
	title,
	action,
	flush = false,
	children,
}: {
	title: string;
	action?: React.ReactNode;
	/** Content runs edge to edge (lists); the header keeps its padding. */
	flush?: boolean;
	children: React.ReactNode;
}) {
	return (
		<Box
			component="section"
			sx={{
				bgcolor: tokens.surface,
				border: `1px solid ${tokens.line}`,
				borderRadius: "16px",
				boxShadow: "0 1px 2px rgba(20,22,30,.04)",
				overflow: "hidden",
				minWidth: 0,
				p: flush ? 0 : { xs: 1.75, sm: 2.25 },
			}}
		>
			<Box
				sx={{
					display: "flex",
					flexWrap: "wrap",
					alignItems: "center",
					justifyContent: "space-between",
					gap: 1,
					// Same header height with or without an action, so titles line up across side-by-side sections.
					minHeight: 40,
					mb: flush ? 0 : 1.5,
					...(flush ? { px: { xs: 1.75, sm: 2.25 }, py: 1.5 } : {}),
				}}
			>
				<Typography component="h2" sx={{ fontSize: 16, fontWeight: 700 }}>
					{title}
				</Typography>
				{action}
			</Box>
			{children}
		</Box>
	);
}

function SubHeading({ children }: { children: React.ReactNode }) {
	return (
		<Typography
			component="h3"
			sx={{ fontSize: 12, fontWeight: 700, letterSpacing: ".06em", textTransform: "uppercase", color: tokens.ink2, mb: 0.75 }}
		>
			{children}
		</Typography>
	);
}

function Hint({ children }: { children: React.ReactNode }) {
	return <Typography sx={{ fontSize: 12.5, color: tokens.ink2 }}>{children}</Typography>;
}

function StatTile({
	label,
	value,
	tone,
	small = false,
	compact = false,
}: {
	label: string;
	value: string;
	tone?: "bad";
	small?: boolean;
	compact?: boolean;
}) {
	return (
		<Box sx={{ p: compact ? "8px 10px" : { xs: "10px 12px", sm: "14px 16px" }, borderRadius: "12px", bgcolor: tokens.ground, minWidth: 0 }}>
			<Typography sx={{ fontSize: { xs: 12, sm: 13 }, color: tokens.ink2, fontWeight: 600 }}>{label}</Typography>
			<Typography
				sx={{
					fontFamily: small ? undefined : DISPLAY_FONT,
					fontWeight: small ? 700 : 800,
					fontSize: small ? { xs: 13.5, sm: 15 } : compact ? 18 : { xs: 22, sm: 28 },
					mt: small ? 0.5 : 0,
					lineHeight: 1.25,
					fontVariantNumeric: "tabular-nums",
					color: tone === "bad" ? BAD : tokens.ink,
				}}
			>
				{value}
			</Typography>
		</Box>
	);
}

/**
 * Phone pager, the same controls as the desktop DataGrid footer: "1–10 of 38", rows per page,
 * and page buttons (40px, first / last page always reachable).
 */
function PhonePager({
	page,
	pageSize,
	total,
	pageSizes,
	onChange,
	noBorder = false,
}: {
	page: number;
	pageSize: number;
	total: number;
	pageSizes: number[];
	onChange: (next: { page: number; pageSize: number }) => void;
	noBorder?: boolean;
}) {
	const pages = Math.max(1, Math.ceil(total / pageSize));
	const from = total === 0 ? 0 : page * pageSize + 1;
	const to = Math.min(total, (page + 1) * pageSize);
	return (
		<Box
			sx={{
				px: 1.75,
				py: 1.25,
				borderTop: noBorder ? "none" : `1px solid ${tokens.line}`,
				display: "flex",
				flexDirection: "column",
				alignItems: "center",
				gap: 1,
			}}
		>
			<Box sx={{ width: "100%", display: "flex", alignItems: "center", justifyContent: "space-between", gap: 1 }}>
				<Typography sx={{ fontSize: 13, color: tokens.ink2, fontVariantNumeric: "tabular-nums" }}>
					<Box component="strong" sx={{ color: tokens.ink }}>
						{from.toLocaleString()}–{to.toLocaleString()}
					</Box>{" "}
					of {total.toLocaleString()}
				</Typography>
				<TextField
					select
					size="small"
					value={pageSize}
					onChange={(e) => onChange({ page: 0, pageSize: Number(e.target.value) })}
					inputProps={{ "aria-label": "Rows per page" }}
					sx={{ "& .MuiInputBase-root": { height: 40, fontSize: 13.5 } }}
				>
					{pageSizes.map((n) => (
						<MenuItem key={n} value={n}>
							{n} / page
						</MenuItem>
					))}
				</TextField>
			</Box>
			{pages > 1 && (
				<Pagination
					count={pages}
					page={Math.min(page + 1, pages)}
					onChange={(_, p) => onChange({ page: p - 1, pageSize })}
					siblingCount={0}
					boundaryCount={1}
					size="large"
					sx={{ "& .MuiPaginationItem-root": { minWidth: 40, height: 40, mx: "1px" } }}
				/>
			)}
		</Box>
	);
}

/** Desktop health numbers on one line: small label over a number, no tile. */
function InlineStat({ label, value, tone }: { label: string; value: string; tone?: "bad" }) {
	return (
		<Box>
			<Typography sx={{ fontSize: 12.5, color: tokens.ink2, fontWeight: 600 }}>{label}</Typography>
			<Typography
				sx={{
					fontFamily: DISPLAY_FONT,
					fontWeight: 800,
					fontSize: 22,
					lineHeight: 1.3,
					fontVariantNumeric: "tabular-nums",
					color: tone === "bad" ? BAD : tokens.ink,
				}}
			>
				{value}
			</Typography>
		</Box>
	);
}

/** Status as a tinted pill: success / completed → green, fail(ed) → red, running / queued → indigo, else grey. */
function StatusPill({ status }: { status?: string | null }) {
	if (!status) {
		return (
			<Box component="span" sx={{ color: tokens.ink2 }}>
				—
			</Box>
		);
	}
	const s = status.toLowerCase();
	const palette =
		s === "success" || s === "completed"
			? { bg: tokens.saveTint, fg: tokens.save }
			: s === "fail" || s === "failed"
				? { bg: BAD_TINT, fg: BAD }
				: s === "running" || s === "queued"
					? { bg: tokens.brandTint, fg: tokens.brand }
					: { bg: "#F0F1F4", fg: tokens.ink2 };
	return (
		<Box
			component="span"
			sx={{
				display: "inline-block",
				flexShrink: 0,
				px: 1.25,
				py: "3px",
				lineHeight: 1.5,
				verticalAlign: "middle",
				borderRadius: 999,
				fontSize: 12,
				fontWeight: 700,
				whiteSpace: "nowrap",
				bgcolor: palette.bg,
				color: palette.fg,
			}}
		>
			{status || "-"}
		</Box>
	);
}

function MatchSide({
	name,
	shopName,
	shopType,
	imageUrl,
}: {
	name: string;
	shopName: string;
	shopType?: number | null;
	imageUrl?: string | null;
}) {
	return (
		<Box
			sx={{
				display: "flex",
				alignItems: "center",
				gap: 1.25,
				p: { xs: 1.25, sm: 1.25 },
				border: { xs: "none", sm: `1px solid ${tokens.line}` },
				borderRadius: "12px",
				minHeight: 64,
				boxSizing: "border-box",
				minWidth: 0,
			}}
		>
			<Avatar
				variant="rounded"
				src={imageUrl ?? undefined}
				alt=""
				sx={{
					width: 44,
					height: 44,
					flexShrink: 0,
					borderRadius: "10px",
					bgcolor: tokens.imageGround,
					border: `1px solid ${tokens.line}`,
					"& img": { objectFit: "contain" },
				}}
			>
				<StoreLogo shopType={shopType} size={22} decorative />
			</Avatar>
			<Box sx={{ minWidth: 0 }}>
				<Typography sx={{ fontSize: 13.5, fontWeight: 600, lineHeight: 1.3, overflowWrap: "anywhere" }}>{name}</Typography>
				<Box sx={{ display: "flex", alignItems: "center", gap: 0.5, mt: 0.25 }}>
					<StoreLogo shopType={shopType} size={14} decorative />
					<Typography sx={{ fontSize: 12, color: tokens.ink2 }}>{shopName}</Typography>
				</Box>
			</Box>
		</Box>
	);
}
