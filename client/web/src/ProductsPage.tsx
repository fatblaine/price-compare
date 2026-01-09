import * as React from "react";
import { useState, useEffect } from "react";
import Box from "@mui/material/Box";
import { DataGrid, GridColDef, GridPaginationModel } from "@mui/x-data-grid";
import {
	TextField,
	Button,
	Stack,
	FormControl,
	InputLabel,
	Select,
	MenuItem,
} from "@mui/material";
import { useMediaQuery, useTheme } from "@mui/material";
import type { SelectChangeEvent } from "@mui/material/Select";
import { SHOP_OPTIONS, shopTypeName } from "./constants/shopTypes";
import { fetchProducts, type ProductRow } from "./api/products";
import { useDebounce } from "./hooks/useDebounce";
import CompareDialog from "./components/CompareDialog";

export default function ProductsPage() {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));
	const isMdDown = useMediaQuery(theme.breakpoints.down("md"));
	const [rows, setRows] = useState<ProductRow[]>([]);
	const [rowCount, setRowCount] = useState(0);
	const [loading, setLoading] = useState(false);
	const [search, setSearch] = useState("");
	const [shopType, setShopType] = useState("");
	const [paginationModel, setPaginationModel] = useState<GridPaginationModel>({
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

	const columns: GridColDef<ProductRow>[] = [
		{ field: "productId", headerName: "ID", width: 200 },
		{
			field: "name",
			headerName: "Product Name",
			flex: 1,
			minWidth: isXs ? 160 : 220,
		},
		{ field: "price", headerName: "Price", width: 100 },
		{
			field: "shopType",
			headerName: "Shop",
			width: 120,
			renderCell: (params) => shopTypeName((params as any).row?.shopType),
		},
		{ field: "size", headerName: "Size", width: 120 },
		{
			field: "compare",
			headerName: "Compare",
			width: 120,
			sortable: false,
			renderCell: (params) => (
				<Button
					variant="contained"
					size="small"
					color="primary"
					onClick={() =>
						handleCompare(
							params.row.productId,
							params.row.name,
							params.row.shopType,
						)
					}
				>
					Compare
				</Button>
			),
		},
	];

	const columnVisibilityModel = React.useMemo(
		() => ({
			productId: !isMdDown, // 隐藏 ID 在中小屏
			size: !isXs, // 超小屏隐藏 Size 列
		}),
		[isMdDown, isXs],
	);

	return (
		<Box sx={{ height: isXs ? "auto" : 600, width: "100%", p: { xs: 1.5, sm: 3 } }}>
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
				<FormControl size="small" sx={{ minWidth: { xs: "100%", sm: 160 } }} fullWidth={isXs}>
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

			<DataGrid
				rows={rows}
				columns={columns}
				columnVisibilityModel={columnVisibilityModel}
				getRowId={(row) => row.productId}
				loading={loading}
				pagination
				paginationMode="server"
				paginationModel={paginationModel}
				onPaginationModelChange={(model) => setPaginationModel(model)}
				rowCount={rowCount}
				pageSizeOptions={[10, 20, 50]}
				disableRowSelectionOnClick
				autoHeight={isXs}
			/>

			<CompareDialog
				open={compareOpen}
				keyword={compareKeyword}
				sourceShop={compareSourceShop}
				onClose={() => setCompareOpen(false)}
			/>
		</Box>
	);
}
