import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";
import { SessionProvider } from "@/contexts/session-provider";
import { ModeNav } from "@/components/shared/mode-nav";
import { DirectionSync } from "@/components/shared/direction-sync";
import { Toaster } from "@/components/ui/sonner";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Liara Docs Assistant",
  description: "Ask, search, and practice with Liara's documentation.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="fa"
      dir="rtl"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
      suppressHydrationWarning
    >
      <body className="flex h-full min-h-screen flex-col">
        <SessionProvider>
          <DirectionSync />
          <ModeNav />
          <main className="flex flex-1 flex-col overflow-hidden pb-14 md:pb-0">{children}</main>
          <Toaster />
        </SessionProvider>
      </body>
    </html>
  );
}
