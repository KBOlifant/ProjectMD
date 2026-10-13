// MOCK: stand-in tech stacks for the static scan provider. Each entry is a list of
// [category label, technology name] pairs. These are NOT measured from any real site;
// mock-scan.ts picks one by hashing the host so the same host always gets the same stack.
export const MOCK_STACKS: [string, string][][] = [
  [["Framework", "Next.js 15"], ["Library", "React 19"], ["Hosting", "Vercel"], ["Styling", "Tailwind CSS"], ["Language", "TypeScript"]],
  [["Framework", "Nuxt 3"], ["Library", "Vue 3"], ["Hosting", "Cloudflare"], ["Build", "Vite"], ["Language", "TypeScript"]],
  [["Framework", "WordPress 6"], ["Language", "PHP 8"], ["Database", "MySQL"], ["CDN", "Cloudflare"], ["Library", "jQuery"]],
  [["Framework", "Astro 4"], ["Hosting", "Cloudflare Pages"], ["Styling", "Tailwind CSS"], ["Language", "TypeScript"]],
  [["Framework", "Django"], ["Language", "Python"], ["Database", "PostgreSQL"], ["Server", "Nginx"]],
];
