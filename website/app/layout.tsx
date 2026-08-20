import type { Metadata } from "next";
import "./globals.css";

const assetPrefix = process.env.NEXT_PUBLIC_BASE_PATH ?? "";
const socialImagePath = `${assetPrefix || "/FiveMCleaner"}/og.png`;

export const metadata: Metadata = {
  metadataBase: new URL("https://marquezinii.github.io/"),
  title: "FiveMCleaner — Otimização transparente para FiveM",
  description:
    "Otimize Windows, FiveM e GTA V com perfis automáticos, progresso claro e rollback. Windows 11 recomendado, com compatibilidade legada para Windows 10 x64.",
  applicationName: "FiveMCleaner",
  keywords: [
    "FiveMCleaner",
    "FiveM",
    "otimização Windows",
    "GTA V",
    "FPS",
    "Windows 11",
  ],
  authors: [{ name: "Felipe Marquezini" }],
  creator: "Felipe Marquezini",
  openGraph: {
    type: "website",
    locale: "pt_BR",
    alternateLocale: "en_US",
    title: "FiveMCleaner — Seu PC mais preparado para o FiveM",
    description:
      "Perfis automáticos, mudanças transparentes e rollback para Windows, FiveM e GTA V.",
    siteName: "FiveMCleaner",
    images: [{ url: socialImagePath, width: 1200, height: 630 }],
  },
  twitter: {
    card: "summary_large_image",
    title: "FiveMCleaner",
    description: "Otimização transparente para FiveM no Windows.",
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
