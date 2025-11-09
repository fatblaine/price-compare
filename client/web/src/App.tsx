import { AppBar, Box, Container, Toolbar, Typography } from "@mui/material";
import React from "react";
import ProductsPage from "./ProductsPage";

function App() {
	return (
		<Box sx={{ bgcolor: "background.default", minHeight: "100vh" }}>
			<AppBar
				position="sticky"
				elevation={0}
				sx={{
					background:
						"linear-gradient(90deg, #0ea5e9 0%, #6366f1 50%, #a855f7 100%)",
				}}
			>
				<Toolbar sx={{ py: { xs: 1, sm: 1.5 } }}>
					<Container
						maxWidth="lg"
						sx={{ display: "flex", alignItems: "center" }}
					>
						<Typography
							variant="h4"
							component="h1"
							sx={{
								flexGrow: 1,
								fontWeight: 800,
								letterSpacing: 0.5,
								textShadow: "0 1px 2px rgba(0,0,0,.25)",
								fontSize: { xs: 22, sm: 26, md: 30, lg: 34 },
								textAlign: { xs: "center", sm: "left" },
							}}
						>
							Price-Compare
						</Typography>
					</Container>
				</Toolbar>
			</AppBar>

			<Container maxWidth="lg" sx={{ py: { xs: 2, md: 3 } }}>
				<ProductsPage />
			</Container>
		</Box>
	);
}

export default App;
