export default {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [2, 'always', [
      'feat', 'fix', 'perf', 'docs', 'chore',
      'refactor', 'test', 'ci', 'build', 'deps',
    ]],
    'header-case': [0],
    'body-case': [0],
    'subject-case': [0],
    'scope-case': [0],
    'type-case': [0],
  },
  ignores: [(commit) => /^Signed-off-by: dependabot\[bot\]/m.test(commit)],
};
