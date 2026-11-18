import * as React from "react";
import {
	Box,
	Button,
	ButtonBase,
	CircularProgress,
	Autocomplete,
	List,
	ListItem,
	Dialog,
	DialogTitle,
	DialogContent,
	DialogActions,
	IconButton,
	TextField,
	Checkbox,
	FormControlLabel,
	Stack,
	Typography,
} from "@mui/material";
import type { AutocompleteInputChangeReason } from "@mui/material/Autocomplete";
import { useTheme, useMediaQuery } from "@mui/material";
import {
	fetchMyReceipts,
	fetchReceiptDetail,
	uploadAndParseReceipt,
	updateReceiptItems,
	deleteReceipt,
	type ParsedReceiptItem,
	type UpdateReceiptItemPayload,
	type ReceiptDetail,
	type ReceiptSummary,
	type UploadAndParseResponse,
} from "./api/receipts";
import { addFavorite } from "./api/favorites";
import { fetchProducts, type ProductRow } from "./api/products";
import { useDebounce } from "./hooks/useDebounce";
import CloseIcon from "@mui/icons-material/Close";
import PhotoCameraOutlinedIcon from "@mui/icons-material/PhotoCameraOutlined";
import FileUploadOutlinedIcon from "@mui/icons-material/FileUploadOutlined";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import EditOutlinedIcon from "@mui/icons-material/EditOutlined";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import ReceiptLongOutlinedIcon from "@mui/icons-material/ReceiptLongOutlined";
import StoreLogo from "./components/StoreLogo";
import ConfirmSheet from "./components/ConfirmSheet";
import { shopTypeFromStoreName } from "./constants/shopTypes";
import { DISPLAY_FONT, tokens } from "./theme";

const OCR_SCANS_KEY = "ocr_scans_today";
const DAILY_SCANS = 3;

/** System monospace for the printed-receipt details (receipt no., time, item numbers). No web font download. */
const MONO = 'ui-monospace, "SFMono-Regular", "Cascadia Mono", Menlo, Consolas, monospace';

function readScansToday(): number {
	const today = new Date().toISOString().slice(0, 10);
	try {
		const raw = localStorage.getItem(OCR_SCANS_KEY);
		if (!raw) return 0;
		const { date, count } = JSON.parse(raw) as { date: string; count: number };
		return date === today ? count : 0;
	} catch {
		return 0;
	}
}

function saveScansToday(count: number): void {
	const today = new Date().toISOString().slice(0, 10);
	try {
		localStorage.setItem(OCR_SCANS_KEY, JSON.stringify({ date: today, count }));
	} catch {
		// Storage can be blocked (private mode); the server still enforces the limit.
	}
}

/** A total of 0 means OCR did not read one (no real receipt is $0.00), so it shows as "—". */
const money = (v?: number | null) =>
	v == null || !Number.isFinite(Number(v)) || Number(v) <= 0 ? "—" : `$${Number(v).toFixed(2)}`;

const toDate = (iso?: string | null): Date | null => {
	if (!iso) return null;
	const d = new Date(iso);
	return Number.isNaN(d.getTime()) ? null : d;
};

/** "Sat 15 Nov, 6:42 pm" */
const receiptDateText = (iso?: string | null) => {
	const d = toDate(iso);
	if (!d) return "Date unknown";
	const sameYear = d.getFullYear() === new Date().getFullYear();
	const day = d.toLocaleDateString("en-AU", {
		weekday: "short",
		day: "numeric",
		month: "short",
		...(sameYear ? {} : { year: "numeric" }),
	});
	const time = d.toLocaleTimeString("en-AU", { hour: "numeric", minute: "2-digit" });
	return `${day}, ${time}`;
};

/** "15/11/26 18:42", printed on the paper. */
const receiptStamp = (iso?: string | null) => {
	const d = toDate(iso);
	if (!d) return "";
	const p = (n: number) => String(n).padStart(2, "0");
	return `${p(d.getDate())}/${p(d.getMonth() + 1)}/${String(d.getFullYear()).slice(2)} ${p(d.getHours())}:${p(d.getMinutes())}`;
};

const monthKey = (iso?: string | null) => {
	const d = toDate(iso);
	return d ? `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}` : "unknown";
};

const monthLabel = (iso?: string | null) => {
	const d = toDate(iso);
	return d ? d.toLocaleDateString("en-AU", { month: "long", year: "numeric" }) : "Date unknown";
};

const receiptNo = (id: number) => String(id).padStart(6, "0");

interface EditableReceiptItem {
	id?: number | null;
	ocrName: string;
	finalName: string;
	matchedProductId: string | null;
	confidence: number;
}

interface FavoriteSelectionItem {
	name: string;
	matchedProductId: string | null;
	selected: boolean;
}

// Receipts as printed paper slips that unfold in place (design 2026-11-19); detail is loaded on first unfold.
export default function MyReceiptsPage() {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));

	const [receipts, setReceipts] = React.useState<ReceiptSummary[]>([]);
	const [openIds, setOpenIds] = React.useState<number[]>([]);
	const [details, setDetails] = React.useState<Record<number, ReceiptDetail | null>>({});
	const [loadingIds, setLoadingIds] = React.useState<number[]>([]);
	const [loadingList, setLoadingList] = React.useState(false);
	const [listError, setListError] = React.useState(false);
	const [uploading, setUploading] = React.useState(false);
	const [uploadError, setUploadError] = React.useState<string | null>(null);
	const fileInputRef = React.useRef<HTMLInputElement | null>(null);
	const [editDialogOpen, setEditDialogOpen] = React.useState(false);
	const [editingReceiptId, setEditingReceiptId] = React.useState<number | null>(null);
	// Offer "add to favourites" only after a fresh upload, not when renaming an old receipt.
	const [editFromUpload, setEditFromUpload] = React.useState(false);
	const [editingItems, setEditingItems] = React.useState<EditableReceiptItem[]>([]);
	const [savingEdit, setSavingEdit] = React.useState(false);
	const [favoriteDialogOpen, setFavoriteDialogOpen] = React.useState(false);
	const [favoriteItems, setFavoriteItems] = React.useState<FavoriteSelectionItem[]>([]);
	const [addingFavorites, setAddingFavorites] = React.useState(false);
	const [productSearchQuery, setProductSearchQuery] = React.useState("");
	const [productOptions, setProductOptions] = React.useState<ProductRow[]>([]);
	const [productSearchLoading, setProductSearchLoading] = React.useState(false);
	const debouncedProductQuery = useDebounce(productSearchQuery, 350);
	const [deletingId, setDeletingId] = React.useState<number | null>(null);
	const [confirmDeleteId, setConfirmDeleteId] = React.useState<number | null>(null);
	const [scansUsedToday, setScansUsedToday] = React.useState<number>(() => readScansToday());

	const loadList = React.useCallback(async () => {
		setLoadingList(true);
		setListError(false);
		try {
			const data = await fetchMyReceipts();
			setReceipts(data);
			return data;
		} catch (e) {
			// eslint-disable-next-line no-console
			console.error("Failed to load receipts", e);
			setListError(true);
			return null;
		} finally {
			setLoadingList(false);
		}
	}, []);

	// Load the list once; unfold the newest receipt so the page never opens empty-looking.
	React.useEffect(() => {
		void loadList().then((data) => {
			if (data && data.length > 0) setOpenIds((prev) => (prev.length ? prev : [data[0].id]));
		});
	}, [loadList]);

	const loadDetail = React.useCallback(async (id: number) => {
		setLoadingIds((prev) => (prev.includes(id) ? prev : [...prev, id]));
		try {
			const d = await fetchReceiptDetail(id);
			setDetails((prev) => ({ ...prev, [id]: d }));
		} catch (e) {
			// eslint-disable-next-line no-console
			console.error("Failed to load receipt detail", e);
			setDetails((prev) => ({ ...prev, [id]: null }));
		} finally {
			setLoadingIds((prev) => prev.filter((x) => x !== id));
		}
	}, []);

	// Fetch detail the first time a receipt is unfolded.
	React.useEffect(() => {
		openIds.forEach((id) => {
			if (!(id in details) && !loadingIds.includes(id)) void loadDetail(id);
		});
	}, [openIds, details, loadingIds, loadDetail]);

	React.useEffect(() => {
		let active = true;

		const run = async () => {
			const q = debouncedProductQuery.trim();
			if (!q) {
				setProductOptions([]);
				return;
			}

			setProductSearchLoading(true);
			try {
				const { items } = await fetchProducts({
					page: 1,
					pageSize: 10,
					name: q,
				});
				if (active) {
					setProductOptions(items);
				}
			} catch (e) {
				// eslint-disable-next-line no-console
				console.error("Failed to search products", e);
				if (active) {
					setProductOptions([]);
				}
			} finally {
				if (active) {
					setProductSearchLoading(false);
				}
			}
		};

		void run();
		return () => {
			active = false;
		};
	}, [debouncedProductQuery]);

	const toggleOpen = (id: number) => {
		setOpenIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
	};

	const handleDeleteConfirm = async () => {
		const id = confirmDeleteId;
		if (id == null) return;
		setDeletingId(id);
		try {
			await deleteReceipt(id);
			setReceipts((prev) => prev.filter((r) => r.id !== id));
			setOpenIds((prev) => prev.filter((x) => x !== id));
			setDetails((prev) => {
				const next = { ...prev };
				delete next[id];
				return next;
			});
			setConfirmDeleteId(null);
		} catch (e) {
			// eslint-disable-next-line no-console
			console.error("Failed to delete receipt", e);
		} finally {
			setDeletingId(null);
		}
	};

	const handleUploadClick = () => {
		if (fileInputRef.current) {
			fileInputRef.current.click();
		}
	};

	const handleFileChange: React.ChangeEventHandler<HTMLInputElement> = async (event) => {
		const file = event.target.files?.[0];
		if (!file) return;

		setUploadError(null);
		setUploading(true);

		try {
			const result: UploadAndParseResponse = await uploadAndParseReceipt(file);

			// Track daily scan quota
			const newCount = scansUsedToday + 1;
			setScansUsedToday(newCount);
			saveScansToday(newCount);

			// Refresh the list and unfold the new receipt at the top.
			await loadList();
			setOpenIds((prev) => [result.receiptId, ...prev.filter((x) => x !== result.receiptId)]);

			// Prepare editable items for the dialog.
			const editable: EditableReceiptItem[] = [];

			if (result.items && result.items.length > 0) {
				result.items.forEach((it: ParsedReceiptItem) => {
					const ocrName = it.ocrName || it.finalName || "";
					const finalName = it.finalName || it.ocrName || "";
					if (!finalName.trim()) {
						return;
					}
					editable.push({
						id: it.receiptItemId,
						ocrName,
						finalName,
						matchedProductId: it.matchedProductId,
						confidence: it.confidence,
					});
				});
			} else if (result.productNames && result.productNames.length > 0) {
				// Fallback for older backend payloads: only productNames are available.
				result.productNames.forEach((name) => {
					if (!name) return;
					editable.push({
						id: null,
						ocrName: name,
						finalName: name,
						matchedProductId: null,
						confidence: 1.0,
					});
				});
			}

			setEditingReceiptId(result.receiptId);
			setEditFromUpload(true);
			setEditingItems(editable);
			setEditDialogOpen(true);
		} catch (e: any) {
			// eslint-disable-next-line no-console
			console.error("Failed to upload and parse receipt", e);
			if (e?.response?.status === 429) {
				setScansUsedToday(DAILY_SCANS);
				saveScansToday(DAILY_SCANS);
				setUploadError("You've reached your daily limit of 3 receipt scans. Please try again tomorrow.");
			} else {
				const message: string | undefined =
					e?.response?.data && typeof e.response.data === "string" ? e.response.data : e?.message;
				setUploadError(message || "Upload failed. Please try again.");
			}
		} finally {
			setUploading(false);
			// reset file input so the same file can be selected again if needed
			event.target.value = "";
		}
	};

	// "Edit names" on any receipt: start from its saved items, keeping each item's product link.
	const handleEditExisting = (detail: ReceiptDetail) => {
		setEditingReceiptId(detail.id);
		setEditFromUpload(false);
		setEditingItems(
			detail.items.map((it) => ({
				id: it.id,
				ocrName: it.originalName ?? "",
				finalName: it.productName ?? "",
				matchedProductId: it.matchedProductId ?? null,
				confidence: it.confidence ?? 1.0,
			})),
		);
		setEditDialogOpen(true);
	};

	const handleCloseEditDialog = () => {
		if (savingEdit) return;
		setEditDialogOpen(false);
	};

	const handleItemInputChange = (index: number, value: string, reason: AutocompleteInputChangeReason) => {
		setEditingItems((prev) =>
			prev.map((item, i) =>
				i === index
					? {
							...item,
							finalName: value,
							matchedProductId: reason === "input" || reason === "clear" ? null : item.matchedProductId,
						}
					: item,
			),
		);

		if (reason === "input") {
			setProductSearchQuery(value);
		}

		if (reason === "clear") {
			setProductSearchQuery("");
		}
	};

	const handleItemSelect = (index: number, value: ProductRow | string | null) => {
		setEditingItems((prev) =>
			prev.map((item, i) => {
				if (i !== index) return item;
				if (typeof value === "string") {
					return {
						...item,
						finalName: value,
						matchedProductId: null,
					};
				}
				if (value) {
					return {
						...item,
						finalName: value.name,
						matchedProductId: value.productId,
					};
				}
				return item;
			}),
		);
	};

	const handleRemoveItem = (index: number) => {
		setEditingItems((prev) => prev.filter((_, i) => i !== index));
	};

	const handleAddItem = () => {
		setEditingItems((prev) => [
			...prev,
			{
				id: null,
				ocrName: "",
				finalName: "",
				matchedProductId: null,
				confidence: 1.0,
			},
		]);
	};

	const saveEditedItems = async () => {
		if (!editingReceiptId) return;

		const payload: UpdateReceiptItemPayload[] = editingItems
			.map((item) => ({
				id: item.id ?? undefined,
				finalName: item.finalName.trim(),
				matchedProductId: item.matchedProductId ?? null,
			}))
			.filter((p) => p.finalName !== "");

		if (payload.length === 0) {
			// Nothing meaningful to save.
			setEditDialogOpen(false);
			return;
		}

		setSavingEdit(true);
		try {
			await updateReceiptItems(editingReceiptId, payload);

			// Refresh the edited receipt's unfolded detail.
			await loadDetail(editingReceiptId);

			// Prepare a separate list for favorites selection.
			const favItems: FavoriteSelectionItem[] = editingItems
				.filter((item) => !!item.matchedProductId)
				.map((item) => ({
					name: (item.finalName || item.ocrName).trim(),
					matchedProductId: item.matchedProductId,
					selected: false,
				}))
				.filter((f) => f.name !== "");

			setEditDialogOpen(false);

			if (editFromUpload && favItems.length > 0) {
				setFavoriteItems(favItems);
				setFavoriteDialogOpen(true);
			}
		} catch (e) {
			// eslint-disable-next-line no-console
			console.error("Failed to save edited receipt items", e);
		} finally {
			setSavingEdit(false);
		}
	};

	const handleToggleFavoriteSelection = (index: number, checked: boolean) => {
		setFavoriteItems((prev) =>
			prev.map((item, i) =>
				i === index
					? {
							...item,
							selected: checked,
						}
					: item,
			),
		);
	};

	const handleSkipFavorites = () => {
		if (addingFavorites) return;
		setFavoriteDialogOpen(false);
	};

	const handleConfirmFavorites = async () => {
		setAddingFavorites(true);
		try {
			const tasks: Promise<void>[] = [];
			favoriteItems.forEach((item) => {
				if (item.selected && item.matchedProductId) {
					tasks.push(addFavorite(item.matchedProductId));
				}
			});

			if (tasks.length > 0) {
				await Promise.allSettled(tasks);
			}

			setFavoriteDialogOpen(false);
		} catch (e) {
			// eslint-disable-next-line no-console
			console.error("Failed to add favorites from receipt", e);
		} finally {
			setAddingFavorites(false);
		}
	};

	// Newest first, grouped by month with the month's total.
	const groups = React.useMemo(() => {
		const sorted = [...receipts].sort(
			(a, b) => (toDate(b.purchaseDate)?.getTime() ?? 0) - (toDate(a.purchaseDate)?.getTime() ?? 0),
		);
		const out: { key: string; label: string; total: number; rows: ReceiptSummary[] }[] = [];
		sorted.forEach((r) => {
			const key = monthKey(r.purchaseDate);
			let g = out.find((x) => x.key === key);
			if (!g) {
				g = { key, label: monthLabel(r.purchaseDate), total: 0, rows: [] };
				out.push(g);
			}
			g.rows.push(r);
			g.total += Number(r.totalAmount) || 0;
		});
		return out;
	}, [receipts]);

	const limitReached = scansUsedToday >= DAILY_SCANS;
	const pendingDelete = receipts.find((r) => r.id === confirmDeleteId) ?? null;

	const quotaDots = (
		<Box component="span" sx={{ display: "inline-flex", alignItems: "center", gap: 0.75, whiteSpace: "nowrap" }}>
			<Box component="span" aria-hidden="true" sx={{ display: "inline-flex", gap: "3px" }}>
				{Array.from({ length: DAILY_SCANS }).map((_, i) => (
					<Box
						key={i}
						component="span"
						sx={{ width: 8, height: 8, borderRadius: "50%", bgcolor: i < scansUsedToday ? tokens.brand : tokens.line }}
					/>
				))}
			</Box>
			<Box component="strong" sx={{ color: tokens.ink, fontVariantNumeric: "tabular-nums" }}>
				{Math.min(scansUsedToday, DAILY_SCANS)} of {DAILY_SCANS}
			</Box>{" "}
			today
		</Box>
	);

	const uploadCard = (
		<Box
			component="section"
			aria-label="Add a receipt"
			sx={{
				bgcolor: tokens.surface,
				border: `1px solid ${tokens.line}`,
				borderRadius: "16px",
				p: { xs: 1.5, md: 2.25 },
				display: "flex",
				flexDirection: "column",
				gap: { xs: 1.25, md: 1.75 },
				boxShadow: tokens.cardShadow,
			}}
		>
			<input
				ref={fileInputRef}
				type="file"
				accept="image/jpeg,image/png,application/pdf"
				style={{ display: "none" }}
				onChange={handleFileChange}
			/>
			{!isXs && (
				<Typography component="h2" sx={{ fontSize: 16, fontWeight: 700, display: { xs: "none", md: "block" } }}>
					Add a receipt
				</Typography>
			)}
			<Button
				variant="contained"
				onClick={handleUploadClick}
				disabled={uploading || limitReached}
				startIcon={
					uploading ? undefined : isXs ? (
						<PhotoCameraOutlinedIcon aria-hidden="true" />
					) : (
						<FileUploadOutlinedIcon aria-hidden="true" />
					)
				}
				sx={{ height: 48, fontSize: 15, fontWeight: 700 }}
			>
				{uploading ? (
					<>
						<CircularProgress size={18} sx={{ color: "inherit", mr: 1 }} />
						Reading receipt…
					</>
				) : isXs ? (
					"Scan a receipt"
				) : (
					"Upload a receipt"
				)}
			</Button>
			<Box
				sx={{
					display: "flex",
					alignItems: "center",
					justifyContent: "space-between",
					gap: 1,
					fontSize: { xs: 12.5, md: 13 },
					color: tokens.ink2,
				}}
			>
				<span>{limitReached ? "Daily limit reached · resets tomorrow" : "Photo or PDF · JPG, PNG"}</span>
				{quotaDots}
			</Box>
			{uploadError && (
				<Typography role="alert" sx={{ fontSize: 13, color: "#B42318" }}>
					{uploadError}
				</Typography>
			)}
		</Box>
	);

	return (
		<Box>
			<Box sx={{ mb: { xs: 1.5, sm: 2.5 } }}>
				<Typography
					component="h1"
					sx={{
						fontFamily: DISPLAY_FONT,
						fontWeight: 800,
						fontSize: { xs: 24, sm: 34 },
						letterSpacing: { xs: "-0.02em", sm: "-0.03em" },
						lineHeight: 1.12,
					}}
				>
					My Receipts
				</Typography>
				{receipts.length > 0 && (
					<Typography sx={{ mt: 0.5, fontSize: { xs: 13, sm: 15 }, color: tokens.ink2 }}>
						<Box component="strong" sx={{ color: tokens.ink, fontWeight: 700 }}>
							{receipts.length}
						</Box>{" "}
						{receipts.length === 1 ? "receipt" : "receipts"} · {isXs ? "tap" : "click"} one to unfold it
					</Typography>
				)}
			</Box>

			<Box
				sx={{
					display: "grid",
					gridTemplateColumns: { xs: "minmax(0, 1fr)", md: "minmax(0, 1fr) 320px" },
					gridTemplateAreas: { xs: '"aside" "main"', md: '"main aside"' },
					alignItems: "start",
					gap: { xs: 1.5, md: 3 },
				}}
			>
				<Box sx={{ gridArea: "aside", display: "flex", flexDirection: "column", gap: 2 }}>
					{uploadCard}
					{groups.length > 0 && (
						<Box
							component="section"
							aria-label="Spending by month"
							sx={{
								display: { xs: "none", md: "flex" },
								flexDirection: "column",
								bgcolor: tokens.surface,
								border: `1px solid ${tokens.line}`,
								borderRadius: "16px",
								p: 2.25,
							}}
						>
							<Typography component="h2" sx={{ fontSize: 16, fontWeight: 700, mb: 1 }}>
								By month
							</Typography>
							{groups.map((g) => (
								<Box
									key={g.key}
									sx={{
										display: "flex",
										justifyContent: "space-between",
										alignItems: "baseline",
										gap: 1,
										py: 1,
										borderTop: `1px solid ${tokens.line}`,
										fontSize: 14,
									}}
								>
									<Box component="span" sx={{ color: tokens.ink2 }}>
										{g.label} · {g.rows.length}
									</Box>
									<Box component="strong" sx={{ fontVariantNumeric: "tabular-nums" }}>
										{money(g.total)}
									</Box>
								</Box>
							))}
						</Box>
					)}
				</Box>

				<Box sx={{ gridArea: "main", minWidth: 0, display: "flex", flexDirection: "column", gap: { xs: 1.5, sm: 2 } }}>
					{loadingList && receipts.length === 0 ? (
						<Box sx={{ display: "flex", justifyContent: "center", py: 8 }}>
							<CircularProgress size={28} />
						</Box>
					) : listError && receipts.length === 0 ? (
						<EmptyBox>
							Couldn't load your receipts.{" "}
							<Button onClick={() => void loadList()} sx={{ height: 40 }}>
								Try again
							</Button>
						</EmptyBox>
					) : receipts.length === 0 ? (
						<EmptyBox>
							<Typography sx={{ fontWeight: 700, color: tokens.ink, mb: 0.5 }}>No receipts yet</Typography>
							Scan a supermarket receipt and the products on it show up here.
						</EmptyBox>
					) : (
						groups.map((g) => (
							<Box component="section" key={g.key} aria-label={g.label} sx={{ display: "flex", flexDirection: "column", gap: { xs: 1.25, sm: 1.5 } }}>
								<Typography
									component="h2"
									sx={{
										mx: 0.5,
										mt: 0.5,
										display: "flex",
										justifyContent: "space-between",
										fontSize: { xs: 12, sm: 12.5 },
										fontWeight: 700,
										letterSpacing: ".06em",
										textTransform: "uppercase",
										color: tokens.ink2,
									}}
								>
									<span>{g.label}</span>
									<Box component="span" sx={{ fontVariantNumeric: "tabular-nums" }}>
										{money(g.total)}
									</Box>
								</Typography>
								{g.rows.map((r) => (
									<ReceiptPaper
										key={r.id}
										summary={r}
										detail={details[r.id]}
										loaded={r.id in details}
										loading={loadingIds.includes(r.id)}
										open={openIds.includes(r.id)}
										compact={isXs}
										onToggle={() => toggleOpen(r.id)}
										onRetry={() => void loadDetail(r.id)}
										onEdit={(d) => handleEditExisting(d)}
										onDelete={() => setConfirmDeleteId(r.id)}
										deleting={deletingId === r.id}
									/>
								))}
							</Box>
						))
					)}
				</Box>
			</Box>

			<ConfirmSheet
				open={Boolean(pendingDelete)}
				title="Delete this receipt?"
				confirmLabel="Delete receipt"
				busy={deletingId != null}
				onClose={() => setConfirmDeleteId(null)}
				onConfirm={() => void handleDeleteConfirm()}
			>
				{pendingDelete && (
					<Typography sx={{ fontSize: 14, lineHeight: 1.5, color: tokens.ink2 }}>
						{pendingDelete.storeName || "Unknown store"} · {receiptDateText(pendingDelete.purchaseDate)} ·{" "}
						{money(pendingDelete.totalAmount)}. The receipt and its recognised products are removed. This can't
						be undone.
					</Typography>
				)}
			</ConfirmSheet>

			<Dialog open={editDialogOpen} onClose={handleCloseEditDialog} fullWidth maxWidth="md" fullScreen={isXs}>
				<DialogTitle
					sx={{
						display: "flex",
						alignItems: "center",
						justifyContent: "space-between",
						pr: 2,
					}}
				>
					<Typography variant="h6" component="span" fontWeight={700}>
						{editFromUpload ? "Review parsed items" : "Edit item names"}
					</Typography>
					<IconButton onClick={handleCloseEditDialog} disabled={savingEdit} aria-label="Close" sx={{ width: 44, height: 44 }}>
						<CloseIcon fontSize="small" />
					</IconButton>
				</DialogTitle>
				<DialogContent dividers>
					<Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
						Check the recognized products and adjust names if needed. Pick a suggestion to link the item to a
						product. These changes will be saved to this receipt.
					</Typography>
					{editingItems.length === 0 ? (
						<Typography variant="body2" color="text.secondary">
							No items were detected for this receipt.
						</Typography>
					) : (
						<Stack spacing={1.5}>
							{editingItems.map((item, index) => (
								<Box
									key={index}
									sx={{
										display: "flex",
										gap: 1,
										alignItems: "flex-start",
									}}
								>
									<Box sx={{ flex: 1, minWidth: 0 }}>
										{item.ocrName && item.ocrName !== item.finalName && (
											<Typography variant="caption" color="text.secondary" sx={{ display: "block", fontFamily: MONO }}>
												OCR: {item.ocrName}
											</Typography>
										)}
										<Autocomplete
											freeSolo
											options={productOptions}
											loading={productSearchLoading}
											filterOptions={(opts) => opts}
											getOptionLabel={(option) => (typeof option === "string" ? option : option.name)}
											inputValue={item.finalName}
											onInputChange={(_, value, reason) => handleItemInputChange(index, value, reason)}
											onChange={(_, value) => handleItemSelect(index, value)}
											renderOption={(props, option) => (
												<li {...props}>{typeof option === "string" ? option : option.name}</li>
											)}
											renderInput={(params) => (
												<TextField
													{...params}
													fullWidth
													size="small"
													margin="dense"
													label={`Item ${index + 1}`}
													InputProps={{
														...params.InputProps,
														endAdornment: (
															<>
																{productSearchLoading ? <CircularProgress size={18} sx={{ mr: 1 }} /> : null}
																{params.InputProps.endAdornment}
															</>
														),
													}}
												/>
											)}
										/>
									</Box>
									<IconButton
										onClick={() => handleRemoveItem(index)}
										aria-label={`Remove item ${index + 1}`}
										sx={{ width: 44, height: 44, mt: 1 }}
									>
										<CloseIcon fontSize="small" />
									</IconButton>
								</Box>
							))}
						</Stack>
					)}
					<Box sx={{ mt: 2 }}>
						<Button variant="text" onClick={handleAddItem} sx={{ height: 44 }}>
							Add item
						</Button>
					</Box>
				</DialogContent>
				<DialogActions sx={{ px: 3, py: 2, gap: 1 }}>
					<Button variant="outlined" onClick={handleCloseEditDialog} disabled={savingEdit} sx={{ height: 44 }}>
						Cancel
					</Button>
					<Button
						variant="contained"
						onClick={() => void saveEditedItems()}
						disabled={savingEdit || !editingReceiptId}
						sx={{ height: 44 }}
					>
						{savingEdit ? "Saving..." : "Save items"}
					</Button>
				</DialogActions>
			</Dialog>

			<Dialog open={favoriteDialogOpen} onClose={handleSkipFavorites} fullWidth maxWidth="sm">
				<DialogTitle
					sx={{
						display: "flex",
						alignItems: "center",
						justifyContent: "space-between",
						pr: 2,
					}}
				>
					<Typography variant="h6" component="span" fontWeight={700}>
						Add to My Favorites
					</Typography>
					<IconButton onClick={handleSkipFavorites} disabled={addingFavorites} aria-label="Close" sx={{ width: 44, height: 44 }}>
						<CloseIcon fontSize="small" />
					</IconButton>
				</DialogTitle>
				<DialogContent dividers>
					<Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
						Select the products from this receipt that you want to track in My Favorites.
					</Typography>
					{favoriteItems.length === 0 ? (
						<Typography variant="body2" color="text.secondary">
							No matched products are available to add as favorites.
						</Typography>
					) : (
						<List dense>
							{favoriteItems.map((item, index) => (
								<ListItem key={index} disableGutters>
									<FormControlLabel
										control={
											<Checkbox
												checked={item.selected}
												onChange={(e) => handleToggleFavoriteSelection(index, e.target.checked)}
											/>
										}
										label={item.name}
									/>
								</ListItem>
							))}
						</List>
					)}
				</DialogContent>
				<DialogActions sx={{ px: 3, py: 2, gap: 1 }}>
					<Button variant="outlined" onClick={handleSkipFavorites} disabled={addingFavorites} sx={{ height: 44 }}>
						Skip
					</Button>
					<Button
						variant="contained"
						onClick={() => void handleConfirmFavorites()}
						disabled={addingFavorites || favoriteItems.length === 0}
						sx={{ height: 44 }}
					>
						{addingFavorites ? <CircularProgress size={20} sx={{ color: "white" }} /> : "Add to favorites"}
					</Button>
				</DialogActions>
			</Dialog>
		</Box>
	);
}

/** One receipt drawn as a printed slip: torn edge with side notches, zig-zag foot, mono print details. */
function ReceiptPaper({
	summary,
	detail,
	loaded,
	loading,
	open,
	compact,
	onToggle,
	onRetry,
	onEdit,
	onDelete,
	deleting,
}: {
	summary: ReceiptSummary;
	detail: ReceiptDetail | null | undefined;
	loaded: boolean;
	loading: boolean;
	open: boolean;
	compact: boolean;
	onToggle: () => void;
	onRetry: () => void;
	onEdit: (detail: ReceiptDetail) => void;
	onDelete: () => void;
	deleting: boolean;
}) {
	const store = summary.storeName?.trim() || "Unknown store";
	const shop = shopTypeFromStoreName(summary.storeName);
	const dateText = receiptDateText(summary.purchaseDate);
	const total = money(summary.totalAmount);
	const no = receiptNo(summary.id);
	const panelId = `receipt-${summary.id}-items`;
	const notch = compact ? 8 : 9;
	const items = detail?.items ?? [];

	return (
		<Box
			component="article"
			className="receipt-paper"
			sx={{
				filter: "drop-shadow(0 1px 1px rgba(20,22,30,.07)) drop-shadow(0 10px 16px rgba(20,22,30,.05))",
				opacity: deleting ? 0.5 : 1,
			}}
		>
			<Box sx={{ bgcolor: tokens.surface, borderRadius: "16px 16px 0 0", p: { xs: "4px 4px 0", sm: "6px 6px 0" } }}>
				<ButtonBase
					onClick={onToggle}
					aria-expanded={open}
					aria-controls={panelId}
					sx={{
						width: "100%",
						minHeight: { xs: 64, sm: 72 },
						px: { xs: 1.25, sm: 1.75 },
						py: 1,
						borderRadius: "12px",
						display: "flex",
						alignItems: "center",
						gap: { xs: 1.5, sm: 1.75 },
						textAlign: "left",
						color: tokens.ink,
						"&:hover": { bgcolor: tokens.imageGround },
						"&.Mui-focusVisible": { outline: `2px solid ${tokens.brand}`, outlineOffset: -2 },
					}}
				>
					{shop != null ? (
						<StoreLogo shopType={shop} size={compact ? 36 : 40} decorative />
					) : (
						<Box
							sx={{
								width: compact ? 36 : 40,
								height: compact ? 36 : 40,
								flexShrink: 0,
								borderRadius: "9px",
								bgcolor: tokens.ground,
								color: tokens.ink2,
								display: "flex",
								alignItems: "center",
								justifyContent: "center",
							}}
						>
							<ReceiptLongOutlinedIcon aria-hidden="true" sx={{ fontSize: 20 }} />
						</Box>
					)}
					<Box sx={{ flex: 1, minWidth: 0, display: "flex", flexDirection: "column", gap: 0.25 }}>
						<Typography sx={{ fontSize: { xs: 15, sm: 16 }, fontWeight: 700, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
							{store}
						</Typography>
						<Typography sx={{ fontSize: { xs: 12.5, sm: 13.5 }, color: tokens.ink2 }}>{dateText}</Typography>
					</Box>
					<Typography
						sx={{
							fontFamily: DISPLAY_FONT,
							fontSize: { xs: 18, sm: 21 },
							fontWeight: 800,
							fontVariantNumeric: "tabular-nums",
							letterSpacing: "-0.01em",
						}}
					>
						{total}
					</Typography>
					<ExpandMoreIcon
						aria-hidden="true"
						sx={{ color: tokens.ink2, fontSize: 20, transition: "transform .15s", transform: open ? "rotate(180deg)" : "none" }}
					/>
				</ButtonBase>
			</Box>

			{/* Torn edge: real cut-outs on both sides so the drop shadow follows them. */}
			<Box
				aria-hidden="true"
				sx={{
					position: "relative",
					height: notch * 2,
					background: `radial-gradient(circle at 0 50%, transparent ${notch}px, ${tokens.surface} ${notch + 0.5}px) left / 51% 100% no-repeat, radial-gradient(circle at 100% 50%, transparent ${notch}px, ${tokens.surface} ${notch + 0.5}px) right / 51% 100% no-repeat`,
					"&::after": {
						content: '""',
						position: "absolute",
						left: notch * 2,
						right: notch * 2,
						top: notch - 1,
						borderTop: `1.5px dashed ${tokens.lineStrong}`,
					},
				}}
			/>

			<Box id={panelId} sx={{ bgcolor: tokens.surface, px: { xs: 1.75, sm: 2.5 }, pb: 1, pt: 0.25 }}>
				{!open ? (
					<Box
						sx={{
							display: "flex",
							justifyContent: "space-between",
							gap: 1.5,
							fontFamily: MONO,
							fontSize: { xs: 11.5, sm: 12 },
							color: tokens.ink2,
						}}
					>
						<span>{detail ? `${detail.items.length} ${detail.items.length === 1 ? "ITEM" : "ITEMS"}` : "SHOW ITEMS"}</span>
						<span>NO. {no}</span>
					</Box>
				) : (
					<Box sx={{ display: "flex", flexDirection: "column", gap: 1.25 }}>
						<Box
							sx={{
								display: "flex",
								justifyContent: "space-between",
								fontFamily: MONO,
								fontSize: { xs: 11, sm: 12 },
								color: tokens.ink2,
								letterSpacing: ".02em",
							}}
						>
							<span>NO. {no}</span>
							<span>{receiptStamp(summary.purchaseDate)}</span>
						</Box>

						{loading && !detail ? (
							<Box sx={{ display: "flex", alignItems: "center", gap: 1, py: 2, color: tokens.ink2, fontSize: 13 }}>
								<CircularProgress size={16} /> Loading items…
							</Box>
						) : loaded && !detail ? (
							<Box sx={{ py: 1.5, fontSize: 13, color: tokens.ink2 }}>
								Couldn't load the items.{" "}
								<Button onClick={onRetry} sx={{ height: 40 }}>
									Try again
								</Button>
							</Box>
						) : items.length === 0 ? (
							<Typography sx={{ py: 1.5, fontSize: 13.5, color: tokens.ink2 }}>
								No product names were read from this receipt.
							</Typography>
						) : (
							<Box
								component="ol"
								aria-label={`Items on ${store} receipt`}
								sx={{
									m: 0,
									p: 0,
									listStyle: "none",
									display: "grid",
									gridTemplateColumns: { xs: "minmax(0, 1fr)", sm: "repeat(auto-fill, minmax(min(100%, 280px), 1fr))" },
									columnGap: 3.5,
								}}
							>
								{items.map((it, i) => (
									<Box
										component="li"
										key={it.id}
										sx={{
											display: "flex",
											alignItems: "baseline",
											gap: 1.25,
											py: { xs: 0.875, sm: 1 },
											borderBottom: `1px dotted ${tokens.lineStrong}`,
										}}
									>
										<Box component="span" sx={{ fontFamily: MONO, fontSize: { xs: 11.5, sm: 12 }, color: tokens.ink2, flexShrink: 0 }}>
											{String(i + 1).padStart(2, "0")}
										</Box>
										<Box component="span" sx={{ fontSize: { xs: 13.5, sm: 14 }, lineHeight: 1.35, minWidth: 0, overflowWrap: "anywhere" }}>
											{it.productName}
										</Box>
									</Box>
								))}
							</Box>
						)}

						<Box
							sx={{
								display: "flex",
								alignItems: "baseline",
								justifyContent: "space-between",
								pt: 0.5,
								pb: 0.25,
								borderBottom: `2px solid ${tokens.ink}`,
							}}
						>
							<Box component="span" sx={{ fontFamily: MONO, fontSize: { xs: 12.5, sm: 13 }, fontWeight: 700, letterSpacing: ".14em" }}>
								TOTAL
							</Box>
							<Box
								component="span"
								sx={{ fontFamily: DISPLAY_FONT, fontSize: { xs: 22, sm: 26 }, fontWeight: 800, fontVariantNumeric: "tabular-nums" }}
							>
								{total}
							</Box>
						</Box>

						<Box
							sx={{
								display: "flex",
								flexDirection: { xs: "column", sm: "row" },
								alignItems: { xs: "stretch", sm: "center" },
								justifyContent: "space-between",
								gap: 1.5,
								pt: 0.25,
							}}
						>
							<Box
								aria-hidden="true"
								sx={{ display: "flex", flexDirection: { xs: "column", sm: "row" }, alignItems: "center", gap: { xs: 0.5, sm: 1.5 } }}
							>
								<Box
									sx={{
										width: { xs: 180, sm: 168 },
										height: { xs: 30, sm: 32 },
										background: `repeating-linear-gradient(90deg, ${tokens.ink} 0 2px, transparent 2px 4px, ${tokens.ink} 4px 5px, transparent 5px 8px, ${tokens.ink} 8px 11px, transparent 11px 12px, ${tokens.ink} 12px 13px, transparent 13px 16px)`,
									}}
								/>
								<Box component="span" sx={{ fontFamily: MONO, fontSize: { xs: 10.5, sm: 11 }, color: tokens.ink2, letterSpacing: ".2em" }}>
									{no} {receiptStamp(summary.purchaseDate).replace(/\D/g, "").slice(0, 6)}
								</Box>
							</Box>
							<Box sx={{ display: "flex", gap: 1 }}>
								<Button
									variant="outlined"
									startIcon={<EditOutlinedIcon aria-hidden="true" />}
									disabled={!detail || deleting}
									onClick={() => detail && onEdit(detail)}
									sx={{ height: 44, flex: { xs: 1, sm: "none" } }}
								>
									Edit names
								</Button>
								<Button
									variant="outlined"
									startIcon={<DeleteOutlineIcon aria-hidden="true" />}
									onClick={onDelete}
									disabled={deleting}
									sx={{
										height: 44,
										color: "#B42318",
										borderColor: "#F3D3D3",
										"&:hover": { borderColor: "#E9A9A9", bgcolor: "#FFF7F7" },
									}}
								>
									Delete
								</Button>
							</Box>
						</Box>
					</Box>
				)}
			</Box>

			{/* Zig-zag foot of the slip. */}
			<Box
				aria-hidden="true"
				sx={{
					height: compact ? 8 : 9,
					background: `linear-gradient(135deg, ${tokens.surface} 25%, transparent 25%) 0 0 / ${compact ? 16 : 18}px ${compact ? 16 : 18}px repeat-x, linear-gradient(225deg, ${tokens.surface} 25%, transparent 25%) 0 0 / ${compact ? 16 : 18}px ${compact ? 16 : 18}px repeat-x`,
				}}
			/>
		</Box>
	);
}

function EmptyBox({ children }: { children: React.ReactNode }) {
	return (
		<Box
			sx={{
				py: { xs: 4, sm: 6 },
				px: 2,
				textAlign: "center",
				bgcolor: tokens.surface,
				border: `1px dashed ${tokens.lineStrong}`,
				borderRadius: "16px",
				color: tokens.ink2,
				fontSize: { xs: 13.5, sm: 14.5 },
				lineHeight: 1.5,
			}}
		>
			{children}
		</Box>
	);
}
