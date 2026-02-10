import * as React from "react";
import { useState, useEffect } from "react";
import Box from "@mui/material/Box";
import {
	TextField,
	Button,
	Stack,
	FormControl,
	InputLabel,
	Select,
	MenuItem,
	Typography,
	Card,
	CardContent,
	Chip,
	Pagination,
	LinearProgress,
	Divider,
} from "@mui/material";
import { useMediaQuery, useTheme } from "@mui/material";
import type { SelectChangeEvent } from "@mui/material/Select";
import { SHOP_OPTIONS, shopTypeName } from "./constants/shopTypes";
import { fetchProducts, type ProductRow } from "./api/products";
import { useDebounce } from "./hooks/useDebounce";
import CompareDialog from "./components/CompareDialog";

function formatSizeValue(value?: number | null) {
	return value == null || Number.isNaN(value) ? "-" : String(value);
}

function formatSizeUnit(value?: string | null) {
	return value && value.trim() !== "" ? value : "-";
}

function formatSize(row: ProductRow) {
	if (row.size && row.size.trim() !== "") return row.size;
	const v = row.sizeValue;
	const u = row.sizeUnit;
	if (v != null && u) return `${v} ${u}`;
	if (v != null) return String(v);
	if (u) return u;
	return "";
}

function formatPrice(price?: number, sizeText?: string) {
	if (price == null || !Number.isFinite(price)) return "-";
	const base = `$${price.toFixed(2)}`;
	if (sizeText) return `${base} / ${sizeText}`;
	return `${base} AUD`;
}

function shopAccent(shopType?: number | string | null) {
	if (shopType === 0 || shopType === "0") return "#c93925";
	if (shopType === 1 || shopType === "1") return "#3f8149";
	return "#4b5563";
}

function ProductImage({
	src,
	alt,
}: {
	src?: string | null;
	alt: string;
}) {
	const [failed, setFailed] = React.useState(false);
	if (!src || failed) {
		const initial =
			alt && alt.trim() !== "" ? alt.trim().charAt(0).toUpperCase() : "?";
		return (
			<Box
				sx={{
					height: 170,
					borderRadius: 2.5,
					bgcolor: "#f5f5f7",
					display: "flex",
					alignItems: "center",
					justifyContent: "center",
				}}
			>
				<Box
					sx={{
						color: "#111111",
						fontSize: 48,
						fontWeight: 600,
					}}
				>
					{initial}
				</Box>
			</Box>
		);
	}

	return (
		<Box
			component="img"
			src={src}
			alt={alt}
			onError={() => setFailed(true)}
			sx={{
				height: 170,
				width: "100%",
				objectFit: "contain",
				borderRadius: 2.5,
				backgroundColor: "#ffffff",
			}}
		/>
	);
}

export default function ProductsPage() {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));
	const isMdDown = useMediaQuery(theme.breakpoints.down("md"));
	const [rows, setRows] = useState<ProductRow[]>([]);
	const [rowCount, setRowCount] = useState(0);
	const [loading, setLoading] = useState(false);
	const [search, setSearch] = useState("");
	const [shopType, setShopType] = useState("");
	const [paginationModel, setPaginationModel] = useState({
		page: 0,
		pageSize: 20,
	});
	const [refreshTick, setRefreshTick] = useState(0);

	// Compare dialog state
	const [compareOpen, setCompareOpen] = useState(false);
	const [compareKeyword, setCompareKeyword] = useState<string>("");
	const [compareSourceShop, setCompareSourceShop] = useState<
		number | undefined
	>(undefined);

	const debouncedName = useDebounce(search, 350);

	const handleCompare = (
		productId: string,
		name?: string,
		shopTypeValue?: number | string | null,
	) => {
		if (!name) {
			alert("No product name available for comparison.");
			return;
		}
		setCompareKeyword(name);
		const source = Number(shopTypeValue ?? NaN);
		setCompareSourceShop(
			Number.isFinite(source) ? (source as number) : undefined,
		);
		setCompareOpen(true);
	};

	// Fetch when filters or pagination changes
	useEffect(() => {
		const doFetch = async () => {
			try {
				setLoading(true);
				const page = paginationModel.page + 1; // backend is 1-based
				const pageSize = paginationModel.pageSize;
				const shopTypeNumber =
					shopType.trim() === "" ? undefined : Number(shopType);
				const { items, total } = await fetchProducts({
					page,
					pageSize,
					name: debouncedName || undefined,
					shopType: Number.isFinite(shopTypeNumber)
						? (shopTypeNumber as number)
						: undefined,
				});
				setRows(items);
				setRowCount(total);
			} catch (e) {
				console.error(e);
			} finally {
				setLoading(false);
			}
		};
		doFetch();
	}, [
		debouncedName,
		shopType,
		paginationModel.page,
		paginationModel.pageSize,
		refreshTick,
	]);

	const totalPages = Math.max(
		1,
		Math.ceil(rowCount / Math.max(1, paginationModel.pageSize)),
	);

	return (
		<Box sx={{ width: "100%" }}>
			<Stack direction={{ xs: "column", sm: "row" }} spacing={2} sx={{ mb: 2 }}>
				<TextField
					label="Search by name"
					variant="outlined"
					size="small"
					fullWidth={isXs}
					value={search}
					onChange={(e) => {
						setSearch(e.target.value);
						setPaginationModel((prev) =>
							prev.page === 0 ? prev : { ...prev, page: 0 },
						);
					}}
				/>
				<FormControl
					size="small"
					sx={{ minWidth: { xs: "100%", sm: 160 } }}
					fullWidth={isXs}
				>
					<InputLabel id="shop-type-label">Shop</InputLabel>
					<Select
						labelId="shop-type-label"
						label="Shop"
						value={shopType}
						onChange={(e: SelectChangeEvent) => {
							const value = e.target.value;
							setShopType(value);
							setPaginationModel((prev) =>
								prev.page === 0 ? prev : { ...prev, page: 0 },
							);
							// Force refresh immediately after changing shop type
							setRefreshTick((v) => v + 1);
						}}
					>
						{SHOP_OPTIONS.map((opt) => (
							<MenuItem key={String(opt.value)} value={opt.value}>
								{opt.label}
							</MenuItem>
						))}
					</Select>
				</FormControl>
				<Button
					variant="contained"
					sx={{ width: { xs: "100%", sm: "auto" } }}
					onClick={() => setRefreshTick((v) => v + 1)}
				>
					Search
				</Button>
			</Stack>

			<Box
				sx={{
					position: "relative",
					borderRadius: 4,
					border: "1px solid #edf1f5",
					bgcolor: "#fafafa",
					p: { xs: 1.5, sm: 2.5 },
					overflow: "hidden",
				}}
			>
				{loading && (
					<LinearProgress
						sx={{
							position: "absolute",
							left: 0,
							right: 0,
							top: 0,
						}}
					/>
				)}
				{rows.length === 0 && !loading ? (
					<Box
						sx={{
							py: 8,
							textAlign: "center",
							color: "text.secondary",
						}}
					>
						<Typography variant="body1" sx={{ fontWeight: 600 }}>
							No products found
						</Typography>
						<Typography variant="body2">
							Try adjusting your filters.
						</Typography>
					</Box>
				) : (
					<Box
						sx={{
							display: "grid",
							gridTemplateColumns: {
								xs: "1fr",
								sm: "repeat(2, minmax(0, 1fr))",
								md: "repeat(3, minmax(0, 1fr))",
								lg: "repeat(4, minmax(0, 1fr))",
							},
							gap: { xs: 2, sm: 2.5, md: 3 },
						}}
					>
						{rows.map((row) => {
							const sizeText = formatSize(row);
							const priceText = formatPrice(row.price, sizeText || undefined);
							const accent = shopAccent(row.shopType);
							return (
								<Card
									key={row.productId}
									elevation={0}
									sx={{
										display: "flex",
										flexDirection: "column",
										borderRadius: 3.5,
										border: "1px solid #e8edf2",
										bgcolor: "#ffffff",
										boxShadow: "0 18px 40px rgba(15, 23, 42, 0.08)",
									}}
								>
									<Box
											sx={{
											px: 2.5,
											pt: 2.5,
											pb: 2,
											background:
												"linear-gradient(180deg, #f5f5f7 0%, #ffffff 100%)",
										}}
									>
										<ProductImage src={row.imageUrl} alt={row.name} />
									</Box>
									<CardContent
											sx={{
											flex: 1,
											display: "flex",
											flexDirection: "column",
											gap: 1.2,
											pt: 2,
											alignItems: "center",
											textAlign: "center",
										}}
									>
										<Typography
											variant="subtitle1"
											fontWeight={700}
											sx={{
												minHeight: 48,
												display: "-webkit-box",
												WebkitLineClamp: 2,
												WebkitBoxOrient: "vertical",
												overflow: "hidden",
												width: "100%",
											}}
										>
											{row.name || "-"}
										</Typography>
										<Stack
											direction="row"
											spacing={1}
											useFlexGap
											flexWrap="wrap"
											justifyContent="center"
										>
											<Chip
												size="small"
												label={shopTypeName(row.shopType)}
												color="default"
												sx={{
													fontWeight: 600,
													color: "#ffffff",
													bgcolor: accent,
												}}
											/>
											{sizeText && (
												<Chip
													size="small"
													variant="outlined"
													label={`Size ${sizeText}`}
													sx={{ borderColor: accent, color: accent }}
												/>
											)}
										</Stack>
										<Box>
											<Typography variant="caption" color="text.secondary">
												Price (with unit)
											</Typography>
											<Typography
												variant="h6"
												fontWeight={700}
												sx={{ color: accent }}
											>
												{priceText}
											</Typography>
										</Box>
										<Divider />
										<Stack spacing={0.3}>
											<Typography variant="body2" color="text.secondary">
												Size value: {formatSizeValue(row.sizeValue)}
											</Typography>
											<Typography variant="body2" color="text.secondary">
												Size unit: {formatSizeUnit(row.sizeUnit)}
											</Typography>
										</Stack>
										<Button
											variant="contained"
											size="small"
											sx={{ mt: "auto", alignSelf: "center" }}
											onClick={() =>
												handleCompare(
													row.productId,
													row.name,
													row.shopType,
												)
											}
										>
											Compare
										</Button>
									</CardContent>
								</Card>
							);
						})}
					</Box>
				)}
			</Box>

			<Stack
				direction={{ xs: "column", sm: "row" }}
				spacing={2}
				sx={{ mt: 2 }}
				alignItems={{ xs: "stretch", sm: "center" }}
				justifyContent="space-between"
			>
				<Typography variant="body2" color="text.secondary">
					{rowCount} products
				</Typography>
				<Stack
					direction={{ xs: "column", sm: "row" }}
					spacing={2}
					alignItems={{ xs: "stretch", sm: "center" }}
				>
					<Pagination
						count={totalPages}
						page={paginationModel.page + 1}
						onChange={(_, page) =>
							setPaginationModel((prev) => ({
								...prev,
								page: page - 1,
							}))
						}
						color="primary"
						size={isMdDown ? "small" : "medium"}
					/>
					<FormControl size="small" sx={{ minWidth: 120 }}>
						<InputLabel id="page-size-label">Page size</InputLabel>
						<Select
							labelId="page-size-label"
							label="Page size"
							value={String(paginationModel.pageSize)}
							onChange={(e) => {
								const nextSize = Number(e.target.value);
								setPaginationModel({ page: 0, pageSize: nextSize });
							}}
						>
							{[10, 20, 50].map((opt) => (
								<MenuItem key={opt} value={String(opt)}>
									{opt}
								</MenuItem>
							))}
						</Select>
					</FormControl>
				</Stack>
			</Stack>

			<CompareDialog
				open={compareOpen}
				keyword={compareKeyword}
				sourceShop={compareSourceShop}
				onClose={() => setCompareOpen(false)}
			/>
		</Box>
	);
}
