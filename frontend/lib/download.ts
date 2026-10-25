// Saves text as a file in the browser through a temporary link: makes a Blob of the given type,
// clicks a link to it, then releases the Blob URL. Nothing is stored in the app or sent anywhere.
export function downloadText(filename: string, text: string, mime: string): void {
  const href = URL.createObjectURL(new Blob([text], { type: mime }));
  const a = document.createElement("a");
  a.href = href;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(href);
}
