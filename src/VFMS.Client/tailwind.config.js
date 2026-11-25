module.exports = {
    content: {
        relative: true,
        files: ['./**/*.{razor,cs}', './wwwroot/index.html', './wwwroot/js/**/*.js', '!./{bin,obj,node_modules}/**'],
    },
    theme: {
        extend: {
            colors: {
                /* Soft Black + Muted Red + Warm Neutral design tokens */
                'vfms-bg':            '#F2EFEC',
                'vfms-surface':       '#F8F5F2',
                'vfms-surface-alt':   '#ECE8E4',
                'vfms-surface-strong':'#FFFFFF',
                'vfms-muted':         '#ECE8E4',
                'vfms-charcoal':      '#252323',
                'vfms-charcoal-soft': '#2B2928',
                'vfms-text':          '#242424',
                'vfms-text-secondary':'#6F6966',
                'vfms-text-subtle':   '#8C8581',
                'vfms-text-muted':    '#8C8581',
                'vfms-primary':       '#B83A3A',
                'vfms-primary-hover': '#9F3030',
                'vfms-primary-dark':  '#7F2929',
                'vfms-primary-soft':  '#F3E4E2',
                'vfms-border':        '#D8D1CD',
                'vfms-border-strong': '#C9C0BB',
                'vfms-dark-olive':    '#252323',
                /* Semantic status */
                'vfms-success': '#16A34A',
                'vfms-warning': '#D97706',
                'vfms-error':   '#DC2626',
                'vfms-neutral': '#6B7280',
            },
            fontFamily: { heading: ['Montserrat', 'sans-serif'], body: ['Inter', 'sans-serif'], sans: ['Inter', 'sans-serif'] }
        }
    },
};
