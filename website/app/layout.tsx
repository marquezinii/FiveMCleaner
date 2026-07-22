import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "FiveMCleaner — Otimização transparente para FiveM",
  description:
    "Otimize Windows, FiveM e GTA V com perfis automáticos, progresso claro e rollback. Runtime incluído para Windows 10 e 11 x64.",
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
  },
  twitter: {
    card: "summary",
    title: "FiveMCleaner",
    description: "Otimização transparente para FiveM no Windows.",
  },
  icons: {
    icon: [{ url: "/icon.png", type: "image/png", sizes: "512x512" }],
    shortcut: "/icon.png",
    apple: "/icon.png",
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
