import Foundation
import Testing
@testable import AgentSpeedCore

/// METRICS 6.9, Agent intelligence: exactly one of live, resting and none, never mixed.
@Suite("Agent intelligence forms")
struct AgentIntelligenceTests {
    static func sums(output: Double, index: Double) -> IntelligenceSums {
        var sums = IntelligenceSums()
        sums.output = output
        sums.gradedOutput = output * index
        return sums
    }

    @Test func liveWheneverTheAgentGeneratedInTheWindow() {
        let grade = IntelligenceGrade(modelClass: .flagship, level: 4)
        let form = AgentIntelligence(Self.sums(output: 40, index: 105.5), grade: grade)
        #expect(form == .live(105.5))
        #expect(form.isLive)
        #expect(form.shown == 106)
    }

    @Test func aLiveValueUnderTheGradeStaysLive() {
        let grade = IntelligenceGrade(modelClass: .flagship, level: 5)
        let form = AgentIntelligence(Self.sums(output: 12, index: 104), grade: grade)
        #expect(form.shown == 104)
        #expect(form.isLive)
    }

    @Test func restingOnTheGradeAndNoneWithoutOne() {
        let grade = IntelligenceGrade(modelClass: .standard, level: 2)
        #expect(AgentIntelligence(.zero, grade: grade) == .resting(grade))
        #expect(AgentIntelligence(.zero, grade: grade).shown == 92)
        #expect(!AgentIntelligence(.zero, grade: grade).isLive)
        #expect(AgentIntelligence(.zero, grade: nil) == .none)
        #expect(AgentIntelligence(.zero, grade: nil).shown == nil)
        // A model-less agent with an estimate is live.
        #expect(AgentIntelligence(Self.sums(output: 20, index: 97), grade: nil).shown == 97)
    }

    @Test func shownRoundsHalfAwayFromZero() {
        #expect(AgentIntelligence.live(124.5).shown == 125)
        #expect(AgentIntelligence.live(124.49).shown == 124)
    }

    /// An agent without a model has no grade of its own, but its in-flight estimate weighs as an unknown model.
    @Test func modelLessAgentsHaveNoGradeButWeighAsUnknown() throws {
        let grader = IntelligenceGrader(table: try IntelligenceCaseTests.table(), agentProviders: [:])
        for model in [nil, ""] as [String?] {
            let agent = EngineAgent(id: "a", source: .claudeCode, project: "p", model: model)
            #expect(grader.ownGrade(of: agent) == nil)
            #expect(grader.grade(of: agent).value == 92)
        }
        let named = EngineAgent(id: "b", source: .claudeCode, project: "p", model: "claude-opus-5-5")
        #expect(grader.ownGrade(of: named) == grader.grade(of: named))
    }
}
