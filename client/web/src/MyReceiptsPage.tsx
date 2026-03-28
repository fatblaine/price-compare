import * as React from "react";
import {
	Box,
	Avatar,
	Button,
	Card,
	CardActionArea,
	CardContent,
	CircularProgress,
	Autocomplete,
	Divider,
	List,
	ListItem,
	ListItemAvatar,
	ListItemText,
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
import DeleteIcon from "@mui/icons-material/Delete";

const OCR_SCANS_KEY = "ocr_scans_today";

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
	localStorage.setItem(OCR_SCANS_KEY, JSON.stringify({ date: today, count }));
}

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

// Basic list-and-detail page to browse user's receipts.
export default function MyReceiptsPage() {
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));

	const [receipts, setReceipts] = React.useState<ReceiptSummary[]>([]);
	const [selectedId, setSelectedId] = React.useState<number | null>(null);
	const [detail, setDetail] = React.useState<ReceiptDetail | null>(null);
	const [loadingList, setLoadingList] = React.useState(false);
	const [loadingDetail, setLoadingDetail] = React.useState(false);
	const [uploading, setUploading] = React.useState(false);
	const [uploadError, setUploadError] = React.useState<string | null>(null);
	const fileInputRef = React.useRef<HTMLInputElement | null>(null);
	const [editDialogOpen, setEditDialogOpen] = React.useState(false);
	const [editingReceiptId, setEditingReceiptId] = React.useState<number | null>(
		null,
	);
	const [editingItems, setEditingItems] = React.useState<EditableReceiptItem[]>(
		[],
	);
	const [savingEdit, setSavingEdit] = React.useState(false);
	const [favoriteDialogOpen, setFavoriteDialogOpen] = React.useState(false);
	const [favoriteItems, setFavoriteItems] = React.useState<FavoriteSelectionItem[]>(
		[],
	);
	const [addingFavorites, setAddingFavorites] = React.useState(false);
	const [productSearchQuery, setProductSearchQuery] = React.useState("");
	const [productOptions, setProductOptions] = React.useState<ProductRow[]>([]);
	const [productSearchLoading, setProductSearchLoading] = React.useState(false);
	const debouncedProductQuery = useDebounce(productSearchQuery, 350);
	const [deletingId, setDeletingId] = React.useState<number | null>(null);
	const [confirmDeleteId, setConfirmDeleteId] = React.useState<number | null>(null);
	const [scansUsedToday, setScansUsedToday] = React.useState<number>(() => readScansToday());

	// Load receipt list once on mount.
	React.useEffect(() => {
		let cancelled = false;
		const run = async () => {
			setLoadingList(true);
			try {
				const data = await fetchMyReceipts();
				if (!cancelled) {
					setReceipts(data);
					if (data.length > 0) {
						setSelectedId((prev) => prev ?? data[0].id);
					}
				}
			} catch (e) {
				// In basic page we only log to console; production UI could show an error banner.
				// eslint-disable-next-line no-console
				console.error("Failed to load receipts", e);
			} finally {
				if (!cancelled) setLoadingList(false);
			}
		};
		void run();
		return () => {
			cancelled = true;
		};
	}, []);

	// Load receipt detail when selection changes.
	React.useEffect(() => {
		if (selectedId == null) {
			setDetail(null);
			return;
		}
		let cancelled = false;
		const run = async () => {
			setLoadingDetail(true);
			try {
				const d = await fetchReceiptDetail(selectedId);
				if (!cancelled) setDetail(d);
			} catch (e) {
				// eslint-disable-next-line no-console
				console.error("Failed to load receipt detail", e);
				if (!cancelled) setDetail(null);
			} finally {
				if (!cancelled) setLoadingDetail(false);
			}
		};
		void run();
		return () => {
			cancelled = true;
		};
	}, [selectedId]);

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

	const handleSelect = (id: number) => {
		setSelectedId(id);
	};

	const handleDeleteConfirm = async (id: number) => {
		setDeletingId(id);
		setConfirmDeleteId(null);
		try {
			await deleteReceipt(id);
			setReceipts((prev) => prev.filter((r) => r.id !== id));
			if (selectedId === id) {
				setSelectedId(null);
				setDetail(null);
			}
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

	const handleFileChange: React.ChangeEventHandler<HTMLInputElement> = async (
		event,
	) => {
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

			// Refresh list and select the new receipt in the sidebar.
			setLoadingList(true);
			const data = await fetchMyReceipts();
			setReceipts(data);
			setSelectedId(result.receiptId);

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
			setEditingItems(editable);
			setEditDialogOpen(true);
		} catch (e: any) {
			// eslint-disable-next-line no-console
			console.error("Failed to upload and parse receipt", e);
			if (e?.response?.status === 429) {
				setScansUsedToday(3);
				saveScansToday(3);
				setUploadError("You've reached your daily limit of 3 receipt scans. Please try again tomorrow.");
			} else {
				const message: string | undefined =
					e?.response?.data && typeof e.response.data === "string"
						? e.response.data
						: e?.message;
				setUploadError(message || "Upload failed. Please try again.");
			}
		} finally {
			setUploading(false);
			setLoadingList(false);
			// reset file input so the same file can be selected again if needed
			event.target.value = "";
		}
	};

	const handleCloseEditDialog = () => {
		if (savingEdit) return;
		setEditDialogOpen(false);
	};

	const handleItemInputChange = (
		index: number,
		value: string,
		reason: AutocompleteInputChangeReason,
	) => {
		setEditingItems((prev) =>
			prev.map((item, i) =>
				i === index
					? {
							...item,
							finalName: value,
							matchedProductId:
								reason === "input" || reason === "clear"
									? null
									: item.matchedProductId,
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

	const handleItemSelect = (
		index: number,
		value: ProductRow | string | null,
	) => {
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

			// Refresh detail for the currently selected receipt.
			if (selectedId != null) {
				const d = await fetchReceiptDetail(selectedId);
				setDetail(d);
			}

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

			if (favItems.length > 0) {
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

	return (
		<Box>
			<Stack
				direction={{ xs: "column", sm: "row" }}
				alignItems={{ xs: "flex-start", sm: "center" }}
				justifyContent="space-between"
				spacing={2}
				sx={{ mb: 2 }}
			>
				<Typography
					variant={isXs ? "h5" : "h4"}
					fontWeight={800}
				>
					My Receipts
				</Typography>
				<Box>
					<input
						ref={fileInputRef}
						type="file"
						accept="image/jpeg,image/png,application/pdf"
						style={{ display: "none" }}
						onChange={handleFileChange}
					/>
					<Button
						variant="contained"
						onClick={handleUploadClick}
						disabled={uploading || scansUsedToday >= 3}
						sx={{
							width: { xs: "100%", sm: "auto" },
						}}
					>
						{uploading ? (
							<CircularProgress size={20} sx={{ color: "white" }} />
						) : (
							"Upload receipt"
						)}
					</Button>
					<Typography
						variant="body2"
						color={scansUsedToday >= 3 ? "error" : "text.secondary"}
						sx={{ mt: 0.5 }}
					>
						{scansUsedToday} / 3 scans used today
					</Typography>
					{uploadError && (
						<Typography
							variant="body2"
							color="error"
							sx={{ mt: 0.5 }}
						>
							{uploadError}
						</Typography>
					)}
				</Box>
			</Stack>

			<Stack
				direction={{ xs: "column", md: "row" }}
				spacing={2}
				alignItems="stretch"
			>
				<Card
					variant="outlined"
					sx={{
						flexBasis: { xs: "100%", md: "35%" },
						maxHeight: 480,
						overflow: "hidden",
					}}
				>
					<CardContent sx={{ p: 0 }}>
						<Box sx={{ p: 2, pb: 1.5 }}>
							<Typography variant="subtitle1" fontWeight={600}>
								Receipts
							</Typography>
							<Typography variant="body2" color="text.secondary">
								Select a receipt to view parsed product names.
							</Typography>
						</Box>
						<Divider />
						<Box sx={{ maxHeight: { xs: "none", md: 420 }, overflowY: { xs: "visible", md: "auto" } }}>
							{loadingList ? (
								<Box
									sx={{
										display: "flex",
										alignItems: "center",
										justifyContent: "center",
										py: 4,
									}}
								>
									<CircularProgress size={24} />
								</Box>
							) : receipts.length === 0 ? (
								<Box sx={{ p: 2 }}>
									<Typography variant="body2" color="text.secondary">
										No receipts found yet.
									</Typography>
								</Box>
							) : (
								<List dense disablePadding>
									{receipts.map((r) => {
										const isActive = r.id === selectedId;
										const dateText = r.purchaseDate
											? new Date(r.purchaseDate).toLocaleString()
											: "-";
										const isConfirming = confirmDeleteId === r.id;
										const isDeleting = deletingId === r.id;
										return (
											<ListItem
												key={r.id}
												disableGutters
												secondaryAction={
													isConfirming ? (
														<Stack direction="row" spacing={0.5} sx={{ pr: 1 }}>
															<Button
																size="small"
																color="error"
																variant="contained"
																disabled={isDeleting}
																onClick={() => void handleDeleteConfirm(r.id)}
																sx={{ minWidth: 0, px: 1, fontSize: 11 }}
															>
																{isDeleting ? (
																	<CircularProgress size={14} sx={{ color: "white" }} />
																) : (
																	"Yes"
																)}
															</Button>
															<Button
																size="small"
																disabled={isDeleting}
																onClick={() => setConfirmDeleteId(null)}
																sx={{ minWidth: 0, px: 1, fontSize: 11 }}
															>
																No
															</Button>
														</Stack>
													) : (
														<IconButton
															size="small"
															aria-label="delete receipt"
															disabled={isDeleting}
															onClick={(e) => {
																e.stopPropagation();
																setConfirmDeleteId(r.id);
															}}
															sx={{ mr: 0.5 }}
														>
															<DeleteIcon fontSize="small" />
														</IconButton>
													)
												}
												sx={{
													bgcolor: isActive ? "action.selected" : "transparent",
													pr: isConfirming ? "96px" : "48px",
												}}
											>
												<CardActionArea
													onClick={() => handleSelect(r.id)}
													sx={{ px: 2, py: 1.25 }}
												>
													<ListItemText
														primary={
															<Typography variant="body1" fontWeight={600}>
																{r.storeName || "Unknown store"}
															</Typography>
														}
														secondary={
															<Typography
																variant="body2"
																color="text.secondary"
															>
																{dateText}
															</Typography>
														}
													/>
												</CardActionArea>
											</ListItem>
										);
									})}
								</List>
							)}
						</Box>
					</CardContent>
				</Card>

				<Card
					variant="outlined"
					sx={{
						flexBasis: { xs: "100%", md: "65%" },
						minHeight: 260,
					}}
				>
					<CardContent sx={{ p: { xs: 2, sm: 3 } }}>
						<Typography variant="subtitle1" fontWeight={600} gutterBottom>
							Receipt detail
						</Typography>
						{loadingDetail ? (
							<Box
								sx={{
									display: "flex",
									alignItems: "center",
									justifyContent: "center",
									minHeight: 160,
								}}
							>
								<CircularProgress size={24} />
							</Box>
						) : !detail ? (
							<Typography variant="body2" color="text.secondary">
								Select a receipt on the left to see its items.
							</Typography>
						) : (
							<Box>
								<Stack
									direction={{ xs: "column", sm: "row" }}
									spacing={2}
									sx={{ mb: 2 }}
								>
									<Box sx={{ flex: 1 }}>
										<Typography variant="body2" color="text.secondary">
											Store
										</Typography>
										<Typography variant="body1" fontWeight={600}>
											{detail.storeName || "Unknown store"}
										</Typography>
									</Box>
									<Box sx={{ flex: 1 }}>
										<Typography variant="body2" color="text.secondary">
											Purchase date
										</Typography>
										<Typography variant="body1" fontWeight={600}>
											{detail.purchaseDate
												? new Date(detail.purchaseDate).toLocaleString()
												: "-"}
										</Typography>
									</Box>
								</Stack>

								<Typography
									variant="body2"
									color="text.secondary"
									sx={{ mb: 1 }}
								>
									Recognized product names
								</Typography>
								{detail.items.length === 0 ? (
									<Typography variant="body2" color="text.secondary">
										No product names were extracted for this receipt.
									</Typography>
								) : (
									<List dense>
										{detail.items.map((it) => (
											<ListItem
												key={it.id}
												disableGutters
												sx={{ gap: 1.5 }}
											>
												<ListItemAvatar sx={{ minWidth: 0 }}>
													<Avatar
														variant="rounded"
														src={it.imageUrl ?? undefined}
														alt={it.productName}
														sx={{
															width: 40,
															height: 40,
															fontSize: 14,
															bgcolor: "action.hover",
														}}
													>
														{it.productName?.[0]?.toUpperCase() ?? "?"}
													</Avatar>
												</ListItemAvatar>
												<ListItemText primary={it.productName} />
											</ListItem>
										))}
									</List>
								)}
							</Box>
						)}
					</CardContent>
				</Card>
			</Stack>

			<Dialog
				open={editDialogOpen}
				onClose={handleCloseEditDialog}
				fullWidth
				maxWidth="md"
			>
				<DialogTitle
					sx={{
						display: "flex",
						alignItems: "center",
						justifyContent: "space-between",
						pr: 2,
					}}
				>
					<Typography variant="h6" fontWeight={700}>
						Review parsed items
					</Typography>
					<IconButton
						onClick={handleCloseEditDialog}
						size="small"
						disabled={savingEdit}
					>
						<CloseIcon fontSize="small" />
					</IconButton>
				</DialogTitle>
				<DialogContent dividers>
					<Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
						Check the recognized products and adjust names if needed. These
						changes will be saved to this receipt.
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
										gap: 2,
										alignItems: "flex-start",
									}}
								>
									<Box sx={{ flex: 1 }}>
										{item.ocrName && (
											<Typography
												variant="caption"
												color="text.secondary"
												sx={{ display: "block" }}
											>
												OCR: {item.ocrName}
											</Typography>
										)}
										<Autocomplete
											freeSolo
											options={productOptions}
											loading={productSearchLoading}
											filterOptions={(opts) => opts}
											getOptionLabel={(option) =>
												typeof option === "string" ? option : option.name
											}
											inputValue={item.finalName}
											onInputChange={(_, value, reason) =>
												handleItemInputChange(index, value, reason)
											}
											onChange={(_, value) => handleItemSelect(index, value)}
											renderOption={(props, option) => (
												<li {...props}>
													{typeof option === "string" ? option : option.name}
												</li>
											)}
											renderInput={(params) => (
												<TextField
													{...params}
													fullWidth
													size="small"
													margin="dense"
													label="Final name"
													InputProps={{
														...params.InputProps,
														endAdornment: (
															<>
																{productSearchLoading ? (
																	<CircularProgress
																		size={18}
																		sx={{ mr: 1 }}
																	/>
																) : null}
																{params.InputProps.endAdornment}
															</>
														),
													}}
												/>
											)}
										/>
									</Box>
									<Box
										sx={{
											display: "flex",
											flexDirection: "column",
											alignItems: "flex-end",
											gap: 0.5,
										}}
									>
										<IconButton
											size="small"
											onClick={() => handleRemoveItem(index)}
										>
											<CloseIcon fontSize="small" />
										</IconButton>
									</Box>
								</Box>
							))}
						</Stack>
					)}
					<Box sx={{ mt: 2 }}>
						<Button variant="text" onClick={handleAddItem}>
							Add item
						</Button>
					</Box>
				</DialogContent>
				<DialogActions>
					<Button onClick={handleCloseEditDialog} disabled={savingEdit}>
						Cancel
					</Button>
					<Button
						onClick={() => void saveEditedItems()}
						disabled={savingEdit || !editingReceiptId}
					>
						{savingEdit ? "Saving..." : "Save items"}
					</Button>
				</DialogActions>
			</Dialog>

			<Dialog
				open={favoriteDialogOpen}
				onClose={handleSkipFavorites}
				fullWidth
				maxWidth="sm"
			>
				<DialogTitle
					sx={{
						display: "flex",
						alignItems: "center",
						justifyContent: "space-between",
						pr: 2,
					}}
				>
					<Typography variant="h6" fontWeight={700}>
						Add to My Favorites
					</Typography>
					<IconButton
						onClick={handleSkipFavorites}
						size="small"
						disabled={addingFavorites}
					>
						<CloseIcon fontSize="small" />
					</IconButton>
				</DialogTitle>
				<DialogContent dividers>
					<Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
						Select the products from this receipt that you want to track in My
						Favorites.
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
												onChange={(e) =>
													handleToggleFavoriteSelection(
														index,
														e.target.checked,
													)
												}
											/>
										}
										label={item.name}
									/>
								</ListItem>
							))}
						</List>
					)}
				</DialogContent>
				<DialogActions>
					<Button onClick={handleSkipFavorites} disabled={addingFavorites}>
						Skip
					</Button>
					<Button
						variant="contained"
						onClick={() => void handleConfirmFavorites()}
						disabled={addingFavorites || favoriteItems.length === 0}
					>
						{addingFavorites ? (
							<CircularProgress size={20} sx={{ color: "white" }} />
						) : (
							"Add to favorites"
						)}
					</Button>
				</DialogActions>
			</Dialog>
		</Box>
	);
}
