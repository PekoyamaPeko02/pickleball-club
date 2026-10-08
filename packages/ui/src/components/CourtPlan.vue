<template>
  <!-- A pickleball court seen from above, to scale: 20 × 44 ft, the net across the middle, a 7 ft "kitchen" on each side of it. -->
  <svg class="plan" :class="{ 'plan--free': free, 'plan--draw': draw }" viewBox="0 0 240 480" role="img" :aria-label="label">
    <rect class="plan__apron" x="1" y="1" width="238" height="478" rx="12" />
    <rect class="plan__surface" x="20" y="20" width="200" height="440" />
    <rect class="plan__kitchen" x="20" y="170" width="200" height="140" />
    <g class="plan__lines" fill="none" stroke-linecap="square">
      <rect x="20" y="20" width="200" height="440" pathLength="1" />
      <line x1="20" y1="170" x2="220" y2="170" pathLength="1" />
      <line x1="20" y1="310" x2="220" y2="310" pathLength="1" />
      <line x1="120" y1="20" x2="120" y2="170" pathLength="1" />
      <line x1="120" y1="310" x2="120" y2="460" pathLength="1" />
    </g>
    <line class="plan__net" x1="10" y1="240" x2="230" y2="240" />
  </svg>
</template>

<script setup lang="ts">
defineOptions({ name: 'CourtPlan' });

withDefaults(
  defineProps<{
    /** What a screen reader hears, e.g. "Court 1, free from 18:00". */
    label: string;
    /** A free court is lit; a taken one is dimmed. */
    free?: boolean;
    /** Draw the lines in once when the plan appears. */
    draw?: boolean;
  }>(),
  { free: true, draw: false },
);
</script>

<style scoped>
/* Colours come from custom properties so the same plan works on the night panel and on a light page. */
.plan { display: block; width: 100%; height: auto; }
.plan__apron { fill: var(--plan-apron, transparent); stroke: var(--plan-apron-line, transparent); stroke-width: 1; }
.plan__surface { fill: var(--plan-taken, var(--pbc-court-dim)); transition: fill 0.4s; }
.plan__kitchen { fill: var(--plan-taken-kitchen, #2a4a70); transition: fill 0.4s; }
.plan--free .plan__surface { fill: var(--plan-surface, var(--pbc-court)); }
.plan--free .plan__kitchen { fill: var(--plan-kitchen, var(--pbc-kitchen)); }
.plan__lines { stroke: var(--plan-line, #fff); stroke-width: 4; opacity: 0.55; }
.plan--free .plan__lines { opacity: 1; }
.plan__net { stroke: var(--plan-net, #fff); stroke-width: 3; stroke-dasharray: 2 5; opacity: 0.8; }

.plan--draw .plan__lines > * { stroke-dasharray: 1; stroke-dashoffset: 1; animation: plan-draw 1.1s cubic-bezier(0.65, 0, 0.35, 1) forwards; }
.plan--draw .plan__lines > *:nth-child(2) { animation-delay: 0.25s; }
.plan--draw .plan__lines > *:nth-child(3) { animation-delay: 0.35s; }
.plan--draw .plan__lines > *:nth-child(4) { animation-delay: 0.5s; }
.plan--draw .plan__lines > *:nth-child(5) { animation-delay: 0.6s; }
@keyframes plan-draw { to { stroke-dashoffset: 0; } }
@media (prefers-reduced-motion: reduce) {
  .plan--draw .plan__lines > * { animation: none; stroke-dashoffset: 0; }
  .plan__surface, .plan__kitchen { transition: none; }
}
</style>
