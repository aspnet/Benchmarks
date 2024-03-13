export default function RootLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <html>
      <head><title>Fortunes</title></head>
      <body>{children}</body>
    </html>
  )
}
