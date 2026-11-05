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
	CircularProgress,
	Button,
	FormControl,
	InputLabel,
	Select,
	MenuItem,
} from "@mui/material";
import { useMediaQuery, useTheme } from "@mui/material";
import CloseIcon from "@mui/icons-material/Close";
import {
	fetchCompareMatches,
	fetchCompareMatchesByProduct,
	buildPairedSeries,
	fetchMergedHistory,
	type CompareMatches,
	type CompareProduct,
	type PairedSeriesPoint,
} from "../api/compare";
import { formatUpdatedDate, type LastUpdated } from "../api/products";
import { ShopTypes, shopColors, shopTypeName } from "../constants/shopTypes";
import { DISPLAY_FONT, tokens } from "../theme";
import StoreChip from "./StoreChip";
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
	sourceProductId?: string;
	sourceProduct?: CompareProduct;
	/** Per-shop refresh times from GET /api/Products/last-updated (BTS-161). */
	lastUpdated?: LastUpdated | null;
	onClose: () => void;
}

function useCompare(
	keyword: string,
	sourceShop?: number,
	sourceProductId?: string,
	sourceProduct?: CompareProduct,
) {
	const [loading, setLoading] = React.useState(false);
	const [data, setData] = React.useState<CompareMatches | null>(null);
	const [error, setError] = React.useState<string | null>(null);

	React.useEffect(() => {
		let cancelled = false;
		async function run() {
			if (sourceProductId) {
				if (!sourceProduct) return;
				setLoading(true);
				setError(null);
				try {
					const targets = await fetchCompareMatchesByProduct(sourceProductId);
					const filteredTargets = targets.filter(
						(t) =>
							!Number.isFinite(sourceProduct.shopType) ||
							t.shopType !== sourceProduct.shopType,
					);
					if (!cancelled) {
						setData({ source: sourceProduct, targets: filteredTargets });
					}
				} catch (e: any) {
					if (!cancelled) setError(e?.message ?? "Failed to load comparison");
				} finally {
					if (!cancelled) setLoading(false);
				}
				return;
			}

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
	}, [keyword, sourceShop, sourceProductId, sourceProduct]);

	return { loading, data, error };
}

const money = (v?: number | null) => (v != null && Number.isFinite(v) ? `$${v.toFixed(2)}` : "-");

function PromoChip({ text }: { text?: string | null }) {
	if (!text) return null;
	return (
		<Box
			component="span"
			title={text}
			sx={{
				alignSelf: "flex-start",
				maxWidth: "100%",
				px: 1,
				py: 0.375,
				borderRadius: 999,
				bgcolor: tokens.promoTint,
				color: tokens.promo,
				fontSize: 12,
				fontWeight: 600,
				overflow: "hidden",
				textOverflow: "ellipsis",
				whiteSpace: "nowrap",
			}}
		>
			{text}
		</Box>
	);
}

/** One store's price: the cheapest is outlined in savings green (design: Compare artboard). */
function StorePriceCard({
	shopType,
	item,
	cheapest,
	muted,
	note,
	promo,
	updated,
}: {
	shopType?: number;
	item?: CompareProduct;
	cheapest: boolean;
	/** The dearer of two priced stores: its price is shown in secondary ink. */
	muted: boolean;
	note?: string | null;
	promo?: string | null;
	updated?: string | null;
}) {
	return (
		<Box
			sx={{
				flex: 1,
				minWidth: 0,
				p: { xs: 2, sm: 2.5 },
				borderRadius: "16px",
				bgcolor: tokens.surface,
				border: cheapest ? `2px solid ${tokens.save}` : `1px solid ${tokens.line}`,
				display: "flex",
				flexDirection: "column",
				gap: 1,
			}}
		>
			<Stack direction="row" alignItems="center" justifyContent="space-between" spacing={1}>
				<StoreChip shopType={shopType} />
				{updated && (
					<Typography component="span" sx={{ fontSize: 12, color: tokens.ink3, whiteSpace: "nowrap" }}>
						Updated {updated}
					</Typography>
				)}
			</Stack>
			{item ? (
				<>
					<Typography
						sx={{
							fontSize: 13,
							color: tokens.ink2,
							lineHeight: 1.35,
							display: "-webkit-box",
							WebkitLineClamp: 2,
							WebkitBoxOrient: "vertical",
							overflow: "hidden",
						}}
					>
						{item.name}
						{item.size ? ` · ${item.size}` : ""}
					</Typography>
					<Typography
						component="p"
						sx={{
							m: 0,
							fontFamily: DISPLAY_FONT,
							fontWeight: 800,
							letterSpacing: "-0.02em",
							fontSize: { xs: 30, sm: 36 },
							lineHeight: 1.1,
							fontVariantNumeric: "tabular-nums",
							color: muted ? tokens.ink2 : tokens.ink,
						}}
					>
						{money(item.price)}
					</Typography>
					<PromoChip text={promo} />
					{note && (
						<Typography sx={{ fontSize: 13, color: tokens.ink2, fontVariantNumeric: "tabular-nums" }}>
							{note}
						</Typography>
					)}
				</>
			) : (
				<Typography sx={{ fontSize: 14, color: tokens.ink2, py: 1 }}>
					No matched product found in this store yet.
				</Typography>
			)}
		</Box>
	);
}

export default function CompareDialog(props: CompareDialogProps) {
	const { open, keyword, sourceShop, sourceProductId, sourceProduct, lastUpdated, onClose } =
		props;
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));
	const { loading, data, error } = useCompare(
		keyword,
		sourceShop,
		sourceProductId,
		sourceProduct,
	);

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
	const [promoText, setPromoText] = React.useState<{
		source?: string | null;
		target?: string | null;
	}>({});
	const fallbackTargetShop = React.useMemo(() => {
		if (!source) return undefined;
		return source.shopType === ShopTypes.COLES
			? ShopTypes.WOOLWORTHS
			: source.shopType === ShopTypes.WOOLWORTHS
				? ShopTypes.COLES
				: undefined;
	}, [source]);
	const targetShop = selectedTarget?.shopType ?? fallbackTargetShop;

	React.useEffect(() => {
		let cancelled = false;
		async function run() {
			if (!source) {
				setSeries([]);
				setPromoText({});
				return;
			}
			setSeriesLoading(true);
			try {
				if (!selectedTarget) {
					const hist = await fetchMergedHistory(source.name, source.shopType);
					const onlySource = hist.points.map((p) => ({
						date: new Date(p.scrapedAt).toLocaleDateString(),
						source: p.currentPrice,
						target: null,
					}));
					if (!cancelled) {
						setSeries(onlySource);
						setPromoText({ source: hist.latestPromoText ?? null, target: null });
					}
				} else {
					const s = await buildPairedSeries(
						{ name: source.name, shopType: source.shopType },
						{ name: selectedTarget.name, shopType: selectedTarget.shopType },
					);
					if (!cancelled) {
						setSeries(s.series);
						setPromoText({
							source: s.sourcePromoText ?? null,
							target: s.targetPromoText ?? null,
						});
					}
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

	// Which store is cheaper, from the two prices already on screen.
	const sourcePrice = source?.price ?? null;
	const targetPrice = selectedTarget?.price ?? null;
	const bothPriced = sourcePrice != null && targetPrice != null;
	const diff = bothPriced ? (targetPrice as number) - (sourcePrice as number) : 0;
	const same = bothPriced && Math.abs(diff) < 0.001;
	const sourceCheapest = bothPriced && !same && diff > 0;
	const targetCheapest = bothPriced && !same && diff < 0;
	const cheapestShop = sourceCheapest ? source?.shopType : targetCheapest ? targetShop : undefined;
	const cheapestPrice = sourceCheapest ? sourcePrice : targetCheapest ? targetPrice : null;
	const otherShop = sourceCheapest ? targetShop : source?.shopType;
	const otherPrice = sourceCheapest ? targetPrice : sourcePrice;
	const saving = Math.abs(diff);
	const savingPct = bothPriced && otherPrice ? Math.round((saving / (otherPrice as number)) * 100) : 0;
	const updatedFor = (shop?: number) =>
		shop == null ? null : formatUpdatedDate(lastUpdated?.shops?.[String(shop)]);

	const sourceColor = shopColors(source?.shopType).main;
	const targetColor = shopColors(targetShop).main;
	const matchedSame = selectedTarget?.matchType === "same_product";

	return (
		<Dialog open={open} onClose={onClose} fullWidth maxWidth="md" fullScreen={isXs}>
			<DialogTitle sx={{ pr: 7, fontSize: { xs: 20, sm: 24 }, lineHeight: 1.2 }}>
				{source?.name ?? keyword}
				<IconButton
					aria-label="close"
					onClick={onClose}
					sx={{ position: "absolute", right: 8, top: 8, width: 44, height: 44 }}
				>
					<CloseIcon />
				</IconButton>
			</DialogTitle>
			<DialogContent dividers sx={{ bgcolor: tokens.ground, borderColor: tokens.line }}>
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
					<Stack spacing={2.5}>
						{(source.size || matchedSame) && (
							<Stack direction="row" spacing={1} useFlexGap flexWrap="wrap">
								{source.size && (
									<Box component="span" sx={{ px: 1.25, py: 0.5, borderRadius: 999, bgcolor: "#F1F2F5", color: tokens.ink2, fontSize: 12, fontWeight: 600 }}>
										{source.size}
									</Box>
								)}
								{matchedSame && (
									<Box component="span" sx={{ px: 1.25, py: 0.5, borderRadius: 999, bgcolor: tokens.brandTint, color: tokens.brandDark, fontSize: 12, fontWeight: 600 }}>
										Matched · same product
									</Box>
								)}
							</Stack>
						)}

						{bothPriced && (
							<Box
								sx={{
									p: { xs: 2, sm: 2.5 },
									borderRadius: "16px",
									bgcolor: tokens.saveTint,
									border: "1px solid #BFE3CD",
								}}
							>
								{same ? (
									<Typography sx={{ fontSize: 15, fontWeight: 700, color: tokens.save }}>
										Same price at both stores: {money(sourcePrice)}
									</Typography>
								) : (
									<>
										<Typography sx={{ fontSize: 12, fontWeight: 700, letterSpacing: ".04em", textTransform: "uppercase", color: tokens.save }}>
											Cheapest right now
										</Typography>
										<Stack direction="row" alignItems="center" spacing={1.25} sx={{ mt: 1 }}>
											<StoreChip shopType={cheapestShop} />
											<Typography
												component="span"
												sx={{ fontFamily: DISPLAY_FONT, fontWeight: 800, letterSpacing: "-0.02em", fontSize: { xs: 28, sm: 34 }, lineHeight: 1, fontVariantNumeric: "tabular-nums" }}
											>
												{money(cheapestPrice)}
											</Typography>
										</Stack>
										<Typography sx={{ mt: 1, fontSize: 15, fontWeight: 700, color: tokens.save, fontVariantNumeric: "tabular-nums" }}>
											Save {money(saving)} ({savingPct}%) vs {shopTypeName(otherShop)}
										</Typography>
									</>
								)}
							</Box>
						)}

						{targets.length > 1 && (
							<FormControl size="small" fullWidth={isXs} sx={{ minWidth: { xs: "100%", sm: 320 }, alignSelf: "flex-start" }}>
								<InputLabel id="target-label">Matched product</InputLabel>
								<Select
									labelId="target-label"
									label="Matched product"
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
												<Typography variant="body2" sx={{ fontWeight: 700, fontVariantNumeric: "tabular-nums" }}>
													{money(t.price)}
												</Typography>
											</Stack>
										</MenuItem>
									))}
								</Select>
							</FormControl>
						)}

						<Box>
							<Typography component="h3" sx={{ fontFamily: DISPLAY_FONT, fontWeight: 700, fontSize: 18, mb: 1.5 }}>
								Price at each store
							</Typography>
							<Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
								<StorePriceCard
									shopType={source.shopType}
									item={source}
									cheapest={sourceCheapest}
									muted={targetCheapest}
									promo={promoText.source}
									updated={updatedFor(source.shopType)}
									note={targetCheapest ? `${money(saving)} more than ${shopTypeName(targetShop)}` : null}
								/>
								<StorePriceCard
									shopType={targetShop}
									item={selectedTarget}
									cheapest={targetCheapest}
									muted={sourceCheapest}
									promo={promoText.target}
									updated={updatedFor(targetShop)}
									note={sourceCheapest ? `${money(saving)} more than ${shopTypeName(source.shopType)}` : null}
								/>
							</Stack>
						</Box>

						<Box
							sx={{
								p: { xs: 2, sm: 2.5 },
								borderRadius: "16px",
								bgcolor: tokens.surface,
								border: `1px solid ${tokens.line}`,
							}}
						>
							<Typography component="h3" sx={{ fontFamily: DISPLAY_FONT, fontWeight: 700, fontSize: 18, mb: 1 }}>
								Price history
							</Typography>
							<Box sx={{ width: "100%", height: { xs: 230, sm: 270 } }}>
								{seriesLoading ? (
									<Stack
										alignItems="center"
										justifyContent="center"
										sx={{ py: 6 }}
									>
										<CircularProgress size={24} />
									</Stack>
								) : series.length === 0 ? (
									<Typography variant="body2" color="text.secondary">
										No price history yet.
									</Typography>
								) : (
									<ResponsiveContainer>
										<LineChart
											data={series}
											margin={{ left: isXs ? 0 : 8, right: 8, top: 8, bottom: 8 }}
										>
											<CartesianGrid vertical={false} stroke={tokens.line} />
											<XAxis
												dataKey="date"
												tick={{ fontSize: 11, fill: tokens.ink3 }}
												tickLine={false}
												axisLine={{ stroke: tokens.line }}
											/>
											<YAxis
												tickFormatter={(v) => `$${v}`}
												width={isXs ? 44 : 56}
												tick={{ fontSize: 11, fill: tokens.ink3 }}
												tickLine={false}
												axisLine={false}
											/>
											<Tooltip
												formatter={(v: any) =>
													v != null ? `$${Number(v).toFixed(2)}` : "-"
												}
											/>
											<Legend iconType="plainline" wrapperStyle={{ fontSize: 13 }} />
											<Line
												type="monotone"
												dataKey="source"
												name={shopTypeName(source.shopType)}
												stroke={sourceColor}
												dot={series.length <= 1}
												strokeWidth={2.5}
											/>
											<Line
												type="monotone"
												dataKey="target"
												name={shopTypeName(targetShop)}
												stroke={targetColor}
												dot={series.length <= 1}
												strokeWidth={2.5}
											/>
										</LineChart>
									</ResponsiveContainer>
								)}
							</Box>
						</Box>
					</Stack>
				)}
			</DialogContent>
			<DialogActions sx={{ px: 3, py: 1.5 }}>
				<Button onClick={onClose} variant="outlined" sx={{ minHeight: 44 }}>
					Close
				</Button>
			</DialogActions>
		</Dialog>
	);
}
