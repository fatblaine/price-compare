import * as React from "react";
import {
	Box,
	Button,
	Card,
	CardActionArea,
	CardContent,
	CircularProgress,
	Divider,
	List,
	ListItem,
	ListItemText,
	Stack,
	Typography,
} from "@mui/material";
import { useTheme, useMediaQuery } from "@mui/material";
import {
	fetchMyReceipts,
	fetchReceiptDetail,
	uploadAndParseReceipt,
	type ReceiptDetail,
	type ReceiptSummary,
	type UploadAndParseResponse,
} from "./api/receipts";

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

	const handleSelect = (id: number) => {
		setSelectedId(id);
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

			setLoadingList(true);
			const data = await fetchMyReceipts();
			setReceipts(data);
			setSelectedId(result.receiptId);
		} catch (e: any) {
			// eslint-disable-next-line no-console
			console.error("Failed to upload and parse receipt", e);
			const message: string | undefined =
				e?.response?.data && typeof e.response.data === "string"
					? e.response.data
					: e?.message;
			setUploadError(message || "Upload failed. Please try again.");
		} finally {
			setUploading(false);
			setLoadingList(false);
			// reset file input so the same file can be selected again if needed
			event.target.value = "";
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
						disabled={uploading}
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
						<Box sx={{ maxHeight: 420, overflowY: "auto" }}>
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
										return (
											<ListItem
												key={r.id}
												disableGutters
												sx={{
													bgcolor: isActive ? "action.selected" : "transparent",
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
									<List dense sx={{ maxHeight: 260, overflowY: "auto" }}>
										{detail.items.map((it) => (
											<ListItem key={it.id} disableGutters>
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
		</Box>
	);
}
