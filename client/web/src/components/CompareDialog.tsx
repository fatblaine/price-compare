import * as React from "react";
import {
	Dialog,
	DialogTitle,
	DialogContent,
	DialogActions,
	IconButton,
	Typography,
	Stack,
	Box,
	Chip,
	CircularProgress,
	Button,
	Divider,
	Card,
	CardContent,
	Avatar,
	FormControl,
	InputLabel,
	Select,
	MenuItem,
} from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import {
	fetchCompareMatches,
	buildPairedSeries,
	fetchMergedHistory,
	type CompareMatches,
	type CompareProduct,
	type PairedSeriesPoint,
} from "../api/compare";
import { ShopTypes } from "../constants/shopTypes";
import {
	ResponsiveContainer,
	LineChart,
	Line,
	XAxis,
	YAxis,
	Tooltip,
	Legend,
	CartesianGrid,
} from "recharts";

export interface CompareDialogProps {
	open: boolean;
	keyword: string;
	sourceShop?: number;
	onClose: () => void;
}

function useCompare(keyword: string, sourceShop?: number) {
	const [loading, setLoading] = React.useState(false);
	const [data, setData] = React.useState<CompareMatches | null>(null);
	const [error, setError] = React.useState<string | null>(null);

	React.useEffect(() => {
		let cancelled = false;
		async function run() {
			if (!keyword || !Number.isFinite(sourceShop)) return;
			setLoading(true);
			setError(null);
			try {
				const res = await fetchCompareMatches(keyword, Number(sourceShop));
				if (!cancelled) setData(res);
			} catch (e: any) {
				if (!cancelled) setError(e?.message ?? "Failed to load comparison");
			} finally {
				if (!cancelled) setLoading(false);
			}
		}
		run();
		return () => {
			cancelled = true;
		};
	}, [keyword, sourceShop]);

	return { loading, data, error };
}

function PriceCard({
	title,
	item,
	accent,
}: {
	title: string;
	item?: CompareProduct;
	accent: string;
}) {
	const priceText = item?.price != null ? `$${item.price.toFixed(2)}` : "-";
	const subtitle = item?.size ?? "";
	return (
		<Card
			variant="outlined"
			sx={{ flex: 1, borderColor: accent, borderWidth: 1.5, borderRadius: 2 }}
		>
			<CardContent>
				<Stack direction="row" spacing={2} alignItems="center">
					<Avatar sx={{ bgcolor: accent, width: 40, height: 40 }}>
						{title[0]}
					</Avatar>
					<Box>
						<Typography variant="subtitle2" color="text.secondary">
							{title}
						</Typography>
						<Typography variant="h5" fontWeight={700}>
							{priceText}
						</Typography>
						{subtitle && (
							<Typography variant="body2" color="text.secondary">
								{subtitle}
							</Typography>
						)}
					</Box>
				</Stack>
			</CardContent>
		</Card>
	);
}

export default function CompareDialog(props: CompareDialogProps) {
	const { open, keyword, sourceShop, onClose } = props;
	const { loading, data, error } = useCompare(keyword, sourceShop);

	const source = data?.source;
	const targets = data?.targets ?? [];
	const [selectedTargetId, setSelectedTargetId] = React.useState<string>("");
	const selectedTarget: CompareProduct | undefined = React.useMemo(
		() =>
			targets.find((t) => `${t.productId}` === selectedTargetId) ?? targets[0],
		[targets, selectedTargetId],
	);

	const [seriesLoading, setSeriesLoading] = React.useState(false);
	const [series, setSeries] = React.useState<PairedSeriesPoint[]>([]);

	React.useEffect(() => {
		let cancelled = false;
		async function run() {
			if (!source) {
				setSeries([]);
				return;
			}
			setSeriesLoading(true);
			try {
				if (!selectedTarget) {
					const hist = await fetchMergedHistory(source.name, source.shopType);
					const onlySource = hist.map((p) => ({
						date: new Date(p.scrapedAt).toLocaleDateString(),
						source: p.currentPrice,
						target: null,
					}));
					if (!cancelled) setSeries(onlySource);
				} else {
					const s = await buildPairedSeries(
						{ name: source.name, shopType: source.shopType },
						{ name: selectedTarget.name, shopType: selectedTarget.shopType },
					);
					if (!cancelled) setSeries(s);
				}
			} finally {
				if (!cancelled) setSeriesLoading(false);
			}
		}
		run();
		return () => {
			cancelled = true;
		};
	}, [
		source?.name,
		source?.shopType,
		selectedTarget?.name,
		selectedTarget?.shopType,
	]);

	return (
		<Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
			<DialogTitle sx={{ pr: 6 }}>
				Compare: {keyword}
				<IconButton
					aria-label="close"
					onClick={onClose}
					sx={{ position: "absolute", right: 8, top: 8 }}
				>
					<CloseIcon />
				</IconButton>
			</DialogTitle>
			<DialogContent dividers>
				{loading && (
					<Stack alignItems="center" justifyContent="center" sx={{ py: 6 }}>
						<CircularProgress />
					</Stack>
				)}
				{!loading && error && (
					<Typography color="error" variant="body2">
						{error}
					</Typography>
				)}
				{!loading && !error && source && (
					<Stack spacing={3}>
						<Stack spacing={2}>
							<Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
								<PriceCard
									title={
										source.shopType === ShopTypes.COLES
											? "Source · Coles"
											: "Source · Woolworths"
									}
									item={source}
									accent="#0ea5e9"
								/>
								<PriceCard
									title={
										selectedTarget?.shopType === ShopTypes.COLES
											? "Target · Coles"
											: "Target · Woolworths"
									}
									item={selectedTarget}
									accent="#22c55e"
								/>
							</Stack>

							{targets.length > 1 && (
								<Stack
									direction={{ xs: "column", sm: "row" }}
									spacing={2}
									alignItems="center"
								>
									<FormControl size="small" sx={{ minWidth: 260 }}>
										<InputLabel id="target-label">Target</InputLabel>
										<Select
											labelId="target-label"
											label="Target"
											value={selectedTargetId || (targets[0]?.productId ?? "")}
											onChange={(e) =>
												setSelectedTargetId(String(e.target.value))
											}
										>
											{targets.map((t) => (
												<MenuItem
													key={String(t.productId)}
													value={String(t.productId)}
												>
													<Stack
														direction="row"
														spacing={1}
														alignItems="center"
														sx={{ width: "100%" }}
													>
														<Typography sx={{ flex: 1 }} noWrap>
															{t.name}
														</Typography>
														<Typography variant="body2" color="text.secondary">
															{t.size}
														</Typography>
														<Chip
															size="small"
															label={
																t.price != null ? `$${t.price.toFixed(2)}` : "-"
															}
														/>
													</Stack>
												</MenuItem>
											))}
										</Select>
									</FormControl>

									{source?.price != null && selectedTarget?.price != null && (
										<Chip
											color={
												selectedTarget.price - source.price < 0
													? "success"
													: "default"
											}
											label={`Diff: ${(selectedTarget.price - source.price) >= 0 ? "+" : ""}${(selectedTarget.price - source.price).toFixed(2)}`}
										/>
									)}
								</Stack>
							)}
						</Stack>

						<Divider />

						<Box>
							<Stack
								direction="row"
								alignItems="center"
								spacing={1}
								sx={{ mb: 1 }}
							>
								<Typography variant="subtitle1" fontWeight={600}>
									Weekly Trend
								</Typography>
								<Chip label="AUD" size="small" />
							</Stack>
							<Box sx={{ width: "100%", height: 280 }}>
								{seriesLoading ? (
									<Stack
										alignItems="center"
										justifyContent="center"
										sx={{ py: 6 }}
									>
										<CircularProgress size={24} />
									</Stack>
								) : (
									<ResponsiveContainer>
										<LineChart
											data={series}
											margin={{ left: 8, right: 8, top: 8, bottom: 8 }}
										>
											<CartesianGrid strokeDasharray="3 3" />
											<XAxis dataKey="date" />
											<YAxis tickFormatter={(v) => `$${v}`} width={60} />
											<Tooltip
												formatter={(v: any) =>
													v != null ? `$${Number(v).toFixed(2)}` : "-"
												}
											/>
											<Legend />
											<Line
												type="monotone"
												dataKey="source"
												name="Source"
												stroke="#0ea5e9"
												dot={false}
												strokeWidth={2}
											/>
											<Line
												type="monotone"
												dataKey="target"
												name="Target"
												stroke="#22c55e"
												dot={false}
												strokeWidth={2}
											/>
										</LineChart>
									</ResponsiveContainer>
								)}
							</Box>
						</Box>
					</Stack>
				)}
			</DialogContent>
			<DialogActions>
				<Button onClick={onClose} variant="contained">
					Close
				</Button>
			</DialogActions>
		</Dialog>
	);
}
