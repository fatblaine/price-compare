import * as React from "react";
import Button from "@mui/material/Button";

interface TourButtonProps {
	onClick: () => void;
	disabled?: boolean;
}

export default function TourButton({ onClick, disabled }: TourButtonProps) {
	return (
		<Button
			variant="outlined"
			size="small"
			onClick={onClick}
			disabled={disabled}
			sx={{ width: { xs: "100%", sm: "auto" }, whiteSpace: "nowrap" }}
		>
			Tour / Help
		</Button>
	);
}
