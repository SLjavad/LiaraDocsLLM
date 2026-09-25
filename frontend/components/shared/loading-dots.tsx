export function LoadingDots() {
  return (
    <span className="inline-flex items-center gap-1" aria-label="loading">
      <span className="bg-muted-foreground h-1.5 w-1.5 animate-bounce rounded-full [animation-delay:-0.3s]" />
      <span className="bg-muted-foreground h-1.5 w-1.5 animate-bounce rounded-full [animation-delay:-0.15s]" />
      <span className="bg-muted-foreground h-1.5 w-1.5 animate-bounce rounded-full" />
    </span>
  );
}
