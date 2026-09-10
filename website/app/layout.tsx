import type { Metadata } from "next";
import "./globals.css";

const assetPrefix = process.env.NEXT_PUBLIC_BASE_PATH ?? "";
const socialImagePath = `${assetPrefix || "/Ralven"}/og.png`;

export const metadata: Metadata = {
  metadataBase: new URL("https://vemryx.com/"),
  title: "Ralven — Gerenciamento do Windows com IA.",
  description:
    "Plataforma de gerenciamento e otimização do Windows com IA para diagnóstico, manutenção, aplicativos, atualizações e automações seguras.",
  applicationName: "Ralven",
  keywords: [
    "Ralven",
    "gerenciamento do Windows",
    "IA para Windows",
    "manutenção do PC",
    "otimização do Windows",
    "rollback",
    "Windows 11",
  ],
  authors: [{ name: "Ralven" }],
  creator: "Ralven",
  openGraph: {
    type: "website",
    locale: "pt_BR",
    alternateLocale: "en_US",
    title: "Ralven — Gerenciamento do Windows com IA.",
    description:
      "Gerenciamento inteligente do Windows com diagnóstico, manutenção, aplicativos, atualizações, otimização e rollback.",
    siteName: "Ralven",
    images: [{ url: socialImagePath, width: 1672, height: 941 }],
  },
  twitter: {
    card: "summary_large_image",
    title: "Ralven",
    description: "Plataforma de gerenciamento e otimização do Windows com IA.",
    images: [socialImagePath],
  },
  icons: {
    icon: [{ url: `${assetPrefix}/icon.png`, type: "image/png", sizes: "512x512" }],
    shortcut: `${assetPrefix}/icon.png`,
    apple: `${assetPrefix}/icon.png`,
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="pt-BR">
      <body>{children}</body>
    </html>
  );
}
